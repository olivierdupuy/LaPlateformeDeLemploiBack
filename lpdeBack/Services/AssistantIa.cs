using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>Ce que le modele pense d'une offre douteuse.</summary>
/// <param name="Risque">De 0 a 100. Ne decide rien : il ordonne la file.</param>
public sealed record AvisModeration(int Risque, string Avis);

/// <summary>
/// La couche mince ou le site parle au modele.
///
/// Tout ce qui rend l'application intelligente est ecrit ailleurs, en
/// regles : <see cref="Correspondance"/> note une paire candidat/offre,
/// <see cref="RequeteLibre"/> lit une recherche, <see cref="QualiteCatalogue"/>
/// repere une arnaque. Aucun de ces trois n'a besoin d'une cle d'API, et
/// c'est voulu : la cle peut manquer, l'API peut tomber, le quota peut
/// etre atteint. Le site doit continuer a fonctionner dans les trois cas,
/// simplement moins bavard.
///
/// Ce service n'ajoute donc jamais une fonctionnalite : il ajoute de la
/// nuance a une fonctionnalite qui marche deja. Il rend « null » a la
/// moindre difficulte, et l'appelant garde son resultat calcule.
///
/// Trois garde-fous, parce qu'un appel de modele se paie :
///
///   **Un cache.** La phrase qui resume une correspondance ne change pas
///   d'une minute a l'autre. Sans cache, afficher une liste de dix offres
///   couterait dix appels, a chaque rafraichissement de page.
///
///   **Un plafond journalier.** Une boucle mal ecrite, un robot
///   d'indexation, un pic de trafic : n'importe lequel des trois viderait
///   le compte en une nuit. Le plafond atteint, le site retombe sur ses
///   regles sans rien dire a personne — c'est exactement le comportement
///   qu'il a quand aucune cle n'est configuree.
///
///   **Un perimetre de donnees.** Le modele recoit du texte d'offre et
///   des phrases deja calculees. Jamais un CV, jamais une candidature,
///   jamais l'identite d'un candidat. Le registre des traitements le dit,
///   et les prompts ci-dessous s'y tiennent : la synthese d'une
///   correspondance recoit les raisons — « 4 competences en commun », « a
///   12 km » — et pas le profil qui les a produites.
/// </summary>
public class AssistantIa
{
    private readonly AiClient _ia;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AssistantIa> _log;
    private readonly int _plafond;

    /// <summary>
    /// Combien d'appels par jour, faute de reglage.
    ///
    /// Deux cents : de quoi couvrir les requetes difficiles et les pages
    /// de detail d'une journee ordinaire sur ce site, et pas de quoi faire
    /// une facture surprise. Se regle par « Ai:AppelsAssistesParJour ».
    /// </summary>
    public const int PlafondParDefaut = 200;

    /// <summary>
    /// Ce qu'on a consomme aujourd'hui, et pour quoi faire.
    ///
    /// La repartition n'est pas un ornement : quand le plafond est atteint
    /// avant midi, la question suivante est « a cause de quoi ». Une
    /// relecture de recherche qui part sur chaque frappe et une synthese
    /// de correspondance sur une fiche d'offre ne se corrigent pas de la
    /// meme facon.
    /// </summary>
    private sealed class Compteur
    {
        public int Valeur;
        public readonly ConcurrentDictionary<string, int> ParUsage = new();
    }

    /// <summary>Le bilan du jour, tel que la console l'affiche.</summary>
    /// <param name="Configure">Un modele est joignable — cle comprise.</param>
    public sealed record Bilan(
        bool Configure,
        bool Disponible,
        int Plafond,
        int Consommes,
        int Restant,
        string? Modele,
        IReadOnlyDictionary<string, int> ParUsage);

    /// <summary>
    /// Ou en est le quota, et a cause de quoi.
    ///
    /// Sans cet etat, « plafond atteint » et « aucun modele configure »
    /// sont indiscernables : dans les deux cas le site se tait et retombe
    /// sur ses regles, ce qui est le comportement voulu — mais laisse
    /// l'administrateur sans explication.
    /// </summary>
    public Bilan Etat()
    {
        var compteur = Compteur_();
        return new Bilan(
            Configure: _ia.IsConfigured,
            Disponible: Disponible,
            Plafond: _plafond,
            Consommes: compteur.Valeur,
            Restant: Restant,
            Modele: _ia.IsConfigured ? _ia.Model : null,
            ParUsage: compteur.ParUsage.ToDictionary(x => x.Key, x => x.Value));
    }

    public AssistantIa(AiClient ia, IMemoryCache cache, IConfiguration config, ILogger<AssistantIa> log)
    {
        _ia = ia;
        _cache = cache;
        _log = log;
        _plafond = config.GetValue<int?>("Ai:AppelsAssistesParJour") ?? PlafondParDefaut;
    }

    /// <summary>
    /// Peut-on esperer une reponse ?
    ///
    /// A consulter avant de composer un prompt : construire la question
    /// coute deja quelque chose, et l'interface a souvent besoin de savoir
    /// s'il faut afficher la mention « analyse assistee » avant meme
    /// d'avoir la reponse.
    /// </summary>
    public bool Disponible => _ia.IsConfigured && Restant > 0;

    /// <summary>Combien d'appels le plafond du jour autorise encore.</summary>
    public int Restant => Math.Max(0, _plafond - Consommes());

    private int Consommes() => Compteur_().Valeur;

    private Compteur Compteur_()
    {
        var jour = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return _cache.GetOrCreate($"ia:appels:{jour}", e =>
        {
            // Minuit UTC : le compteur disparait de lui-meme, il n'y a
            // aucune remise a zero a planifier ni a oublier.
            e.AbsoluteExpiration = DateTime.UtcNow.Date.AddDays(1);
            return new Compteur();
        })!;
    }

    // ══════════════════════════════════════
    //  L'appel, et tout ce qui l'entoure
    // ══════════════════════════════════════

    /// <summary>
    /// Un appel au modele, protege.
    ///
    /// Le cache est consulte avant le plafond : une reponse deja connue ne
    /// consomme rien et doit rester servie meme quand le quota du jour est
    /// epuise.
    /// </summary>
    /// <param name="usage">
    /// A quoi sert cet appel. Sert au bilan : quand le quota est epuise
    /// avant midi, c'est la premiere chose qu'on veut savoir.
    /// </param>
    private async Task<string?> Demander(
        string usage, string cle, string consigne, string question,
        double temperature, int maxTokens, CancellationToken ct)
    {
        if (_cache.TryGetValue<string>(cle, out var connu)) return connu;
        if (!_ia.IsConfigured) return null;

        var compteur = Compteur_();
        if (Interlocked.Increment(ref compteur.Valeur) > _plafond)
        {
            // Une fois par jour suffit a le signaler : au-dela, ce
            // journal noierait tout le reste.
            if (compteur.Valeur == _plafond + 1)
                _log.LogWarning(
                    "Plafond d'appels assistes atteint ({Plafond}/jour). "
                    + "Le site continue sur ses regles jusqu'a minuit.", _plafond);
            return null;
        }

        compteur.ParUsage.AddOrUpdate(usage, 1, (_, n) => n + 1);

        try
        {
            var r = await _ia.ChatAsync(consigne, question, temperature, maxTokens,
                jsonMode: true, cancellationToken: ct);

            if (!r.Ok || string.IsNullOrWhiteSpace(r.Content))
            {
                _log.LogInformation("Assistance indisponible : {Erreur}", r.Error);
                return null;
            }

            // Deux heures : assez pour absorber une navigation et les
            // rafraichissements de page, assez peu pour qu'une offre
            // modifiee ne traine pas sa vieille analyse toute la journee.
            _cache.Set(cle, r.Content, TimeSpan.FromHours(2));
            return r.Content;
        }
        catch (Exception ex)
        {
            // Rien de ce que fait ce service n'est indispensable. Une
            // exception ici ne doit pas remonter jusqu'a une page de
            // resultats : elle transformerait un confort en panne.
            _log.LogWarning(ex, "Assistance : appel abandonne.");
            return null;
        }
    }

    private static string Empreinte(string valeur) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valeur)))[..16];

    private static JsonElement? Lire(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Texte(JsonElement e, string champ) =>
        e.TryGetProperty(champ, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() is { Length: > 0 } s ? s : null
            : null;

    private static int? Entier(JsonElement e, string champ) =>
        e.TryGetProperty(champ, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.TryGetInt32(out var n) ? n : null
            : null;

    private static bool? Booleen(JsonElement e, string champ) =>
        e.TryGetProperty(champ, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    // ══════════════════════════════════════
    //  1. Relire une recherche
    // ══════════════════════════════════════

    /// <summary>
    /// Reprendre ce que les regles n'ont pas su prendre.
    ///
    /// Le modele ne rejuge rien : les champs deja remplis par
    /// <see cref="RequeteLibre"/> lui sont donnes comme acquis et ne sont
    /// jamais ecrases. Il ne travaille que sur le reliquat. C'est ce qui
    /// garantit qu'activer une cle d'API ne change pas le resultat des
    /// recherches qui fonctionnaient — il ne peut qu'en rattraper
    /// d'autres.
    /// </summary>
    public async Task<Requete> Relire(string texte, Requete regles, CancellationToken ct = default)
    {
        if (!regles.MeriteUneRelecture) return regles;

        var json = await Demander(
            "relecture de recherche",
            $"ia:req:{Empreinte(texte)}",
            "Tu analyses des recherches d'emploi ecrites en francais par des particuliers. "
            + "Tu extrais uniquement ce qui est explicitement dit ou clairement implique. "
            + "Tu n'inventes jamais un lieu, un salaire ni un type de contrat. "
            + "Tu reponds UNIQUEMENT par un objet JSON valide.",
            ConsigneRequete(texte, regles),
            temperature: 0, maxTokens: 400, ct);

        var e = Lire(json);
        if (e is null) return regles;

        var r = e.Value;

        // Le metier doit tomber dans la nomenclature du site : une famille
        // inventee par le modele ne correspondrait a aucune offre, et le
        // candidat verrait une page vide en croyant avoir ete compris.
        var metier = Texte(r, "metier");
        if (metier is not null && !LexiqueMetiers.Familles.Contains(metier)) metier = null;

        // Le lieu doit etre une ville que le geocodeur connait, sinon le
        // filtre par rayon n'a pas de centre.
        var lieu = Texte(r, "lieu");
        if (lieu is not null && GeoUtils.Trouver(lieu) is null) lieu = null;

        var enrichi = regles with
        {
            Metier = regles.Metier ?? metier,
            Contrat = regles.Contrat ?? (Texte(r, "contrat") is { } c ? LexiqueMetiers.Contrat(c) : null),
            Lieu = regles.Lieu ?? lieu,
            Distanciel = regles.Distanciel ?? Booleen(r, "distanciel"),
            SalaireAnnuelMinimum = regles.SalaireAnnuelMinimum ?? Entier(r, "salaireAnnuelMinimum"),
        };

        // Un rayon par defaut des qu'une ville est venue du modele : elle
        // sort d'une phrase du genre « pas trop loin de Perpignan », qui
        // dit une proximite sans la chiffrer.
        if (regles.Lieu is null && enrichi.Lieu is not null && enrichi.RayonKm is null)
            enrichi = enrichi with { RayonKm = RequeteLibre.RayonParDefaut };

        // Ce que la relecture a ajoute se dit au candidat comme le reste :
        // un filtre applique sans etre annonce ne peut pas etre retire.
        var compris = regles.Compris.ToList();
        compris.AddRange(Nouveautes(regles, enrichi));

        return enrichi with { Compris = compris };
    }

    // Litteral brut a double « $ » : dans cette forme, « {{ }} » delimite
    // une interpolation et une accolade seule reste litterale. C'est ce
    // qu'il faut ici, ou le prompt se termine par un gabarit JSON — un
    // simple « $ » lisait « {"metier" » comme du code et ne compilait pas.
    private static string ConsigneRequete(string texte, Requete regles) =>
        $$"""
          Recherche du candidat :
          « {{texte}} »

          Deja identifie par nos regles, a considerer comme acquis :
          - metier : {{regles.Metier ?? "non identifie"}}
          - contrat : {{regles.Contrat ?? "non identifie"}}
          - lieu : {{regles.Lieu ?? "non identifie"}}
          - teletravail : {{(regles.Distanciel is bool d ? d ? "oui" : "non" : "non identifie")}}
          - salaire annuel minimum : {{regles.SalaireAnnuelMinimum?.ToString() ?? "non identifie"}}

          Complete UNIQUEMENT les champs marques « non identifie ». Laisse a
          null tout ce que la phrase ne dit pas.

          Le metier doit etre choisi exactement dans cette liste, ou null :
          {{string.Join(", ", LexiqueMetiers.Familles)}}

          Le contrat doit valoir CDI, CDD, Alternance, Stage, Interim,
          Freelance, ou null. Le lieu doit etre une commune francaise citee
          ou clairement designee, ou null. Le salaire doit etre un entier en
          euros bruts annuels, ou null.

          Reponds par :
          {"metier": ..., "contrat": ..., "lieu": ..., "distanciel": ..., "salaireAnnuelMinimum": ...}
          """;

    /// <summary>Ce que la relecture a ajoute, dit au candidat comme le reste.</summary>
    private static IEnumerable<string> Nouveautes(Requete avant, Requete apres)
    {
        if (avant.Metier is null && apres.Metier is not null)
            yield return $"métier : {apres.Metier.ToLowerInvariant()}";

        if (avant.Contrat is null && apres.Contrat is not null)
            yield return apres.Contrat.ToLowerInvariant();

        if (avant.Lieu is null && apres.Lieu is not null)
            yield return apres.RayonKm is int r
                ? $"à moins de {r} km de {apres.Lieu}"
                : $"à {apres.Lieu}";

        if (avant.Distanciel is null && apres.Distanciel == true)
            yield return "télétravail";

        if (avant.SalaireAnnuelMinimum is null && apres.SalaireAnnuelMinimum is int s)
            yield return $"à partir de {Correspondance.Euros(s)} par an";
    }

    // ══════════════════════════════════════
    //  2. Resumer une correspondance
    // ══════════════════════════════════════

    /// <summary>
    /// Une phrase qui dit ce que la liste de criteres dit deja, mais
    /// autrement.
    ///
    /// A reserver a la page de detail d'une offre. Sur une liste, ce
    /// serait un appel par ligne : le cache absorberait les visites
    /// suivantes, jamais la premiere, et une page de vingt offres partirait
    /// a vingt appels.
    ///
    /// Le modele ne voit ni le profil, ni le nom, ni le CV : seulement
    /// l'intitule du poste et les phrases que le calcul a produites.
    /// </summary>
    public async Task<string?> Resumer(
        Rapprochement r, string titreOffre, CancellationToken ct = default)
    {
        if (r.Raisons.Count == 0 && r.Reserves.Count == 0) return null;

        var matiere = string.Join(" | ", r.Raisons) + " || " + string.Join(" | ", r.Reserves);

        var json = await Demander(
            "synthèse de correspondance",
            $"ia:corr:{Empreinte(titreOffre + matiere)}",
            "Tu expliques a un candidat pourquoi une offre lui est proposee. "
            + "Tu ne disposes que des elements fournis et tu n'en inventes aucun. "
            + "Tu vouvoies le candidat et tu ecris sobrement, sans superlatif ni "
            + "point d'exclamation. Tu reponds UNIQUEMENT par un objet JSON valide.",
            $$"""
              Poste : {{titreOffre}}

              Points favorables :
              {{(r.Raisons.Count > 0 ? string.Join("\n", r.Raisons.Select(x => "- " + x)) : "- aucun")}}

              Points de vigilance :
              {{(r.Reserves.Count > 0 ? string.Join("\n", r.Reserves.Select(x => "- " + x)) : "- aucun")}}

              Ecris deux phrases au plus, en francais, qui resument honnetement
              la situation pour ce candidat. Ne repete pas les listes telles
              quelles : dis ce qu'elles signifient ensemble. Si les points de
              vigilance sont serieux, dis-le.

              Reponds par : {"resume": "..."}
              """,
            temperature: 0.3, maxTokens: 300, ct);

        return Lire(json) is { } e ? Texte(e, "resume") : null;
    }

    // ══════════════════════════════════════
    //  3. Second avis de moderation
    // ══════════════════════════════════════

    /// <summary>
    /// Ce que les regles ne savent pas voir.
    ///
    /// <see cref="QualiteCatalogue"/> reconnait des motifs — demande
    /// d'argent, coordonnees bancaires, messagerie privee — et decide
    /// seul de ce qui entre en file de moderation. Il ne sait pas lire une
    /// annonce dont chaque phrase est anodine et dont l'ensemble ne tient
    /// pas debout : un poste de « chargé de transferts » a 6 000 € par
    /// mois sans experience requise, avec un employeur sans adresse.
    ///
    /// L'avis rendu ne bloque ni ne publie rien. Il ordonne la file :
    /// aujourd'hui un moderateur la prend dans l'ordre d'arrivee, ce qui
    /// revient a traiter en dernier ce qui est le plus urgent des que la
    /// file s'allonge.
    /// </summary>
    public async Task<AvisModeration?> Moderer(
        JobOffer offre, int scoreRegles, string? motifRegles, CancellationToken ct = default)
    {
        var description = offre.Description ?? string.Empty;
        if (description.Length > 4000) description = description[..4000] + "…";

        var json = await Demander(
            "avis de modération",
            $"ia:mod:{offre.Id}:{Empreinte(offre.Title + description)}",
            "Tu assistes la moderation d'un site d'emploi francais. Tu evalues le risque "
            + "qu'une annonce soit frauduleuse, trompeuse ou illegale. Tu es mesure : la "
            + "grande majorite des annonces sont honnetes, et une annonce maladroite n'est "
            + "pas une arnaque. Tu reponds UNIQUEMENT par un objet JSON valide.",
            $$"""
              Intitule : {{offre.Title}}
              Entreprise : {{offre.Company}}
              Lieu : {{offre.Location}}
              Contrat : {{offre.ContractType}}
              Salaire annonce : {{offre.Salary ?? "non precise"}}
              Experience demandee : {{offre.ExperienceRequired ?? "non precisee"}}

              Description :
              {{description}}

              Nos regles automatiques ont note cette annonce {{scoreRegles}}/100
              au risque de fraude{{(motifRegles is null ? "" : $", pour ce motif : {motifRegles}")}}.

              Donne ton propre avis. Cite ce qui, dans le texte, le justifie.
              Si l'annonce te parait normale, dis-le et note bas.

              Reponds par : {"risque": <entier 0-100>, "avis": "<deux phrases au plus>"}
              """,
            temperature: 0, maxTokens: 400, ct);

        if (Lire(json) is not { } e) return null;

        var risque = Entier(e, "risque");
        var avis = Texte(e, "avis");
        if (risque is null || avis is null) return null;

        return new AvisModeration(Math.Clamp(risque.Value, 0, 100), avis);
    }
}
