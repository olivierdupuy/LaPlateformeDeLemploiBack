using System.Text.RegularExpressions;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Ce qu'on a compris d'une recherche tapee en clair.
/// </summary>
/// <param name="Reste">
/// Les mots qui n'ont ete rattaches a aucun filtre, dans leur forme
/// d'origine — accents compris. Ce ne sont pas des dechets : ce sont les
/// mots-clefs, et c'est eux qu'on cherche en plein texte. Ils gardent
/// leurs accents parce que la base, elle, en a : chercher
/// « developpeur » sans accent dans une colonne collationnee en
/// « CP1_CI_AS » ne trouve pas « Developpeur ».
/// </param>
/// <param name="MotsClefs">
/// Le reliquat, decoupe et debarrasse des mots vides. C'est ce qu'on
/// envoie a la base. « recherche », « poste », « equipe » n'y figurent
/// pas : les passer a un « LIKE » sur la description ramene le catalogue
/// entier, ce qui revient a ne pas filtrer tout en le faisant payer.
/// Les mots gardent leur forme d'origine, accents compris — la colonne,
/// elle, en a.
/// </param>
/// <param name="Compris">
/// Ce qui a ete reconnu, dit en francais. A afficher au candidat : une
/// recherche qui applique des filtres qu'il n'a pas vus passer et qu'il
/// ne peut pas retirer est une recherche qui ment.
/// </param>
public sealed record Requete(
    string? Reste,
    IReadOnlyList<string> MotsClefs,
    string? Metier,
    string? Contrat,
    string? Lieu,
    int? RayonKm,
    bool? Distanciel,
    int? SalaireAnnuelMinimum,
    IReadOnlyList<string> Compris)
{
    /// <summary>Un filtre au moins a ete tire de la requete.</summary>
    public bool ADesFiltres =>
        Metier is not null || Contrat is not null || Lieu is not null
        || Distanciel is not null || SalaireAnnuelMinimum is not null;

    /// <summary>
    /// Vaut-il la peine de faire relire cette requete par un modele ?
    ///
    /// Oui dans un seul cas : il reste une phrase entiere que les regles
    /// n'ont pas su classer. Sur « developpeur react perpignan », elles
    /// ont tout compris et un appel serait de l'argent jete ; sur « je
    /// voudrais bosser dans le soin aupres des personnes agees, pas trop
    /// loin de chez moi », elles ne tiennent presque rien.
    /// </summary>
    public bool MeriteUneRelecture =>
        Reste is not null
        && Reste.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4;
}

/// <summary>
/// Lire une recherche ecrite en francais.
///
/// La recherche du site prend un mot-clef et l'envoie a la base sous la
/// forme « Title.Contains(x) || Company.Contains(x) ||
/// Description.Contains(x) ». Tape « developpeur react alternance
/// perpignan », un candidat obtient donc les annonces qui contiennent
/// cette phrase entiere, c'est-a-dire aucune. Il n'y a pas de message :
/// juste une page vide, et la conclusion qu'il n'y a pas de travail.
///
/// Ce service transforme la phrase en filtres — metier, contrat, lieu,
/// rayon, salaire, teletravail — et rend les mots restants comme
/// mots-clefs. Tout se fait par des regles : le vocabulaire du domaine
/// vit dans <see cref="LexiqueMetiers"/>, les villes dans
/// <see cref="GeoUtils"/>. Un modele de langage n'intervient qu'apres, et
/// seulement sur ce que ces regles n'ont pas su prendre — jamais pour
/// defaire ce qu'elles ont compris. C'est ce qui permet a la recherche de
/// fonctionner identiquement quand aucune cle d'API n'est configuree.
/// </summary>
public static class RequeteLibre
{
    /// <summary>
    /// Rayon retenu quand le candidat dit « autour de » sans donner de
    /// nombre. Vingt-cinq kilometres : de quoi couvrir une agglomeration
    /// et sa premiere couronne, soit ce que « autour de » veut dire pour
    /// quelqu'un qui fera le trajet tous les jours.
    /// </summary>
    public const int RayonParDefaut = 25;

    /// <summary>Un mot de la requete, dans ses deux formes.</summary>
    private sealed class Jeton
    {
        public required string Brut { get; init; }
        public required string Plat { get; init; }
        public bool Pris { get; set; }
    }

    public static Requete Analyser(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return new Requete(
                null, Array.Empty<string>(), null, null, null, null, null, null,
                Array.Empty<string>());

        var jetons = Decouper(texte);
        var compris = new List<string>();

        // L'ordre compte : du plus specifique au plus general. « a 20 km
        // de Perpignan » doit etre lu comme un rayon avant que
        // « Perpignan » ne soit lu comme un lieu simple, et « 35 k » comme
        // un salaire avant que « 35 » ne finisse en mot-clef.
        var salaire = LireSalaire(jetons, compris);
        var (lieu, rayon) = LireLieu(jetons, compris);
        var contrat = LireContrat(jetons, compris);
        var distanciel = LireDistanciel(jetons, compris);
        var metier = LireMetier(jetons, compris);

        var libres = jetons.Where(j => !j.Pris).ToList();
        var reste = string.Join(' ', libres.Select(j => j.Brut)).Trim();

        var motsClefs = libres
            .Where(j => !LexiqueMetiers.EstMotVide(j.Plat))
            .Select(j => j.Brut)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Requete(
            string.IsNullOrWhiteSpace(reste) ? null : reste,
            motsClefs,
            metier, contrat, lieu, rayon, distanciel, salaire, compris);
    }

    // ══════════════════════════════════════
    //  Decoupage
    // ══════════════════════════════════════

    private static List<Jeton> Decouper(string texte)
    {
        // « 35 000 » est un seul nombre ecrit a la francaise. Sans ce
        // recollage, il arrive en deux jetons — « 35 » et « 000 » — et
        // aucune expression de salaire ne le reconnait.
        texte = Regex.Replace(texte, @"(?<=\b\d{1,3})[  ](?=\d{3}\b)", "");

        return texte
            .Split(new[] { ' ', '\t', '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Trim('.', '!', '?', '(', ')', '"', '\''))
            .Where(m => m.Length > 0)
            .Select(m => new Jeton { Brut = m, Plat = LexiqueMetiers.Aplatir(m) })
            .Where(j => j.Plat.Length > 0)
            .ToList();
    }

    /// <summary>Le texte aplati de ce qui n'a pas encore ete pris.</summary>
    private static string Libre(List<Jeton> jetons) =>
        string.Join(' ', jetons.Where(j => !j.Pris).Select(j => j.Plat));

    /// <summary>
    /// Marque comme prise la premiere suite de jetons libres qui forme
    /// cette phrase.
    ///
    /// Le rapprochement se fait sur la forme aplatie, la consommation sur
    /// les jetons d'origine : c'est ainsi qu'une ville ecrite
    /// « Aix-en-Provence » en un seul mot et « Aix en Provence » en trois
    /// se reconnaissent pareil, et disparaissent pareil du reliquat.
    /// </summary>
    private static bool Consommer(List<Jeton> jetons, string phrase)
    {
        phrase = phrase.Trim();
        if (phrase.Length == 0) return false;

        var libres = jetons.Where(j => !j.Pris).ToList();

        for (var debut = 0; debut < libres.Count; debut++)
        {
            var accumule = string.Empty;
            for (var fin = debut; fin < libres.Count; fin++)
            {
                accumule = accumule.Length == 0 ? libres[fin].Plat : accumule + " " + libres[fin].Plat;

                if (accumule.Length > phrase.Length) break;
                if (accumule != phrase) continue;

                for (var k = debut; k <= fin; k++) libres[k].Pris = true;
                return true;
            }
        }

        return false;
    }

    // ══════════════════════════════════════
    //  Salaire
    // ══════════════════════════════════════

    private const string Amorce = @"(?:plus de |a partir de |au moins |minimum |mini |des |superieur a )?";

    private static int? LireSalaire(List<Jeton> jetons, List<string> compris)
    {
        var plat = Libre(jetons);

        // « 45k », « 45 k€ », « a partir de 38k »
        var m = Regex.Match(plat, $@"\b{Amorce}(\d{{1,3}})\s*k\b(?:\s*(?:euros?|e))?");
        int? montant = null;
        string? periode = "an";

        if (m.Success && int.TryParse(m.Groups[1].Value, out var milliers))
        {
            montant = milliers * 1000;
        }
        else
        {
            // « 45000 euros », « 2200 e par mois », « 14 euros de l'heure »
            m = Regex.Match(plat,
                $@"\b{Amorce}(\d{{2,6}})\s*(?:euros?|e)\b(?:\s*(?:par |de l ?)?(mois|an|annee|heure|mensuel|annuel))?");
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out var brut)) return null;

            montant = brut;
            periode = m.Groups[2].Success ? m.Groups[2].Value : null;
        }

        // Une periode explicite ailleurs dans la phrase l'emporte sur le
        // defaut : « 2500 par mois » et « 2500 » ne veulent pas dire la
        // meme chose, et lire le second comme un salaire annuel ferait
        // disparaitre toutes les offres correctes.
        if (periode is null or "an")
        {
            if (Regex.IsMatch(plat, @"\b(par mois|mensuel|mensuels|net mensuel)\b")) periode = "mois";
            else if (Regex.IsMatch(plat, @"\b(de l heure|par heure|horaire|taux horaire)\b")) periode = "heure";
        }

        var annuel = Correspondance.AnnuelBrut(montant, periode);
        if (annuel is null) return null;

        Consommer(jetons, m.Value.Trim());
        compris.Add($"à partir de {Correspondance.Euros(annuel.Value)} par an");

        return annuel;
    }

    // ══════════════════════════════════════
    //  Lieu et rayon
    // ══════════════════════════════════════

    private static (string? Lieu, int? Rayon) LireLieu(List<Jeton> jetons, List<string> compris)
    {
        var plat = Libre(jetons);
        int? rayon = null;

        // « a 20 km », « dans un rayon de 30 kilometres »
        var m = Regex.Match(plat, @"\b(?:a |dans un rayon de |rayon de |rayon )(\d{1,3})\s*(?:km|kilometres?)\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
        {
            rayon = n;
            Consommer(jetons, m.Value.Trim());
        }

        // « autour de », « pres de », « aux alentours de » : une intention
        // de proximite sans chiffre.
        var proximite = Regex.Match(Libre(jetons),
            // Ni « secteur » ni « region » : « secteur bancaire » et
            // « region parisienne » ne parlent pas de la meme chose, et
            // les prendre pour une intention de proximite retirait un bon
            // mot-clef du reliquat au profit d'un rayon invente.
            @"\b(autour de|pres de|proche de|a proximite de|aux alentours de)\b");
        if (proximite.Success)
        {
            rayon ??= RayonParDefaut;
            Consommer(jetons, proximite.Value.Trim());
        }

        var ville = GeoUtils.Trouver(Libre(jetons));
        if (ville is null)
        {
            // Un rayon sans ville ne veut rien dire : on le laisse tomber
            // plutot que de l'appliquer a un centre inconnu.
            return (null, null);
        }

        Consommer(jetons, LexiqueMetiers.Aplatir(ville.Value.Nom));

        compris.Add(rayon is int r
            ? $"à moins de {r} km de {ville.Value.Nom}"
            : $"à {ville.Value.Nom}");

        return (ville.Value.Nom, rayon);
    }

    // ══════════════════════════════════════
    //  Contrat, distanciel, metier
    // ══════════════════════════════════════

    private static string? LireContrat(List<Jeton> jetons, List<string> compris)
    {
        var contrat = LexiqueMetiers.Contrat(Libre(jetons));
        if (contrat is null) return null;

        // On retire le mot qui a declenche la reconnaissance, pas le
        // libelle canonique : le candidat a pu ecrire « apprentissage »,
        // et « Alternance » n'apparait nulle part dans sa phrase.
        foreach (var jeton in jetons.Where(j => !j.Pris).ToList())
            if (LexiqueMetiers.Contrat(jeton.Plat) == contrat)
            {
                jeton.Pris = true;
                break;
            }

        compris.Add(contrat.ToLowerInvariant());
        return contrat;
    }

    private static bool? LireDistanciel(List<Jeton> jetons, List<string> compris)
    {
        if (!LexiqueMetiers.ParleDeDistance(Libre(jetons))) return null;

        foreach (var jeton in jetons.Where(j => !j.Pris).ToList())
            if (LexiqueMetiers.ParleDeDistance(jeton.Plat))
                jeton.Pris = true;

        // « a distance » tient en deux jetons dont aucun ne se reconnait
        // seul : on repasse sur les paires.
        Consommer(jetons, "a distance");
        Consommer(jetons, "home office");
        Consommer(jetons, "full remote");

        compris.Add("télétravail");
        return true;
    }

    // ══════════════════════════════════════
    //  Pertinence
    // ══════════════════════════════════════

    /// <summary>
    /// A quel point cette offre repond a cette recherche.
    ///
    /// Le tri « pertinence » du site n'en etait pas un : il classait par
    /// « a la une », puis « urgent », puis date, sans jamais regarder ce
    /// que le candidat avait tape. Deux offres, l'une intitulee exactement
    /// comme sa recherche et l'autre la mentionnant au detour d'un
    /// paragraphe, sortaient dans l'ordre de leur publication.
    ///
    /// Ou le mot apparait compte autant que sa presence. Un intitule est
    /// choisi ; une description est ecrite au kilometre, et beaucoup
    /// d'annonces y citent des technologies qu'elles n'emploient pas
    /// (« notre stack : PHP, un peu de Python, du Go bientot »).
    ///
    /// La comparaison se fait sur la forme aplatie, sans accents : c'est
    /// le seul endroit ou l'on peut rattraper le candidat qui tape
    /// « developpeur » quand la base contient « Développeur ». Le filtre
    /// SQL, lui, subit la collation de la colonne.
    /// </summary>
    public static int Pertinence(Requete requete, JobOffer offre)
    {
        var score = 0;

        var titre = LexiqueMetiers.Aplatir(offre.Title);
        var etiquettes = LexiqueMetiers.Aplatir($"{offre.Tags} {offre.Category}");
        var entreprise = LexiqueMetiers.Aplatir(offre.Company);

        foreach (var mot in requete.MotsClefs)
        {
            var plat = LexiqueMetiers.Aplatir(mot);
            if (plat.Length == 0) continue;

            if (titre.Contains(plat, StringComparison.Ordinal)) score += 25;
            else if (etiquettes.Contains(plat, StringComparison.Ordinal)) score += 12;
            else if (entreprise.Contains(plat, StringComparison.Ordinal)) score += 6;
            // La description n'est pas aplatie : elle fait plusieurs
            // milliers de caracteres et on en note des milliers par
            // recherche. Une comparaison insensible a la casse sur la
            // chaine d'origine coute cent fois moins et rapporte presque
            // autant, ce niveau ne valant que trois points.
            else if (offre.Description?.Contains(mot, StringComparison.OrdinalIgnoreCase) == true) score += 3;
        }

        // Le bon metier vaut plus qu'un mot bien place : c'est lui qui
        // distingue un poste de developpeur d'une annonce de commercial
        // qui vend des logiciels.
        if (requete.Metier is not null
            && LexiqueMetiers.Metier(offre.Title) == requete.Metier) score += 30;

        // La fraicheur departage a pertinence egale. Elle ne remonte pas
        // une offre hors sujet : huit points ne rattrapent pas un
        // intitule qui ne correspond pas.
        var jours = (DateTime.UtcNow - offre.CreatedAt).TotalDays;
        if (jours <= 7) score += 8;
        else if (jours <= 30) score += 4;

        // Ce que le site mettait deja en avant, conserve mais ramene a sa
        // place : un coup de pouce, pas le critere principal.
        if (offre.IsFeatured) score += 6;
        if (offre.IsUrgent) score += 4;

        return score;
    }

    private static string? LireMetier(List<Jeton> jetons, List<string> compris)
    {
        var metier = LexiqueMetiers.Metier(Libre(jetons));
        if (metier is null) return null;

        // Le metier ne consomme rien. « developpeur » designe la famille
        // et reste un excellent mot-clef : le retirer du reliquat ferait
        // remonter toutes les annonces de la famille avec le meme rang,
        // alors que celles dont l'intitule porte le mot sont meilleures.
        compris.Add($"métier : {metier.ToLowerInvariant()}");
        return metier;
    }
}
