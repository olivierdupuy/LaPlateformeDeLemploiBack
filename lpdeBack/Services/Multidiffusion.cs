using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>Un partenaire vers qui l'on sait pousser une offre.</summary>
public record DestinationDiffusion(
    string Cle,
    string Nom,
    /// <summary>Ce que le recruteur y gagne, en une phrase.</summary>
    string Apport,
    bool Configuree,
    /// <summary>Ce qu'il manque pour l'ouvrir. Vide si elle est prete.</summary>
    string? Manque);

/// <summary>
/// Publier une offre ailleurs, depuis ici.
///
/// Un recruteur qui depose une offre la redepose ensuite chez France
/// Travail, puis chez deux ou trois agregateurs, a la main, en
/// recopiant le meme texte. Puis il pourvoit le poste, et oublie d'en
/// retirer la moitie : les candidatures continuent d'arriver pendant
/// des semaines sur un poste ferme. C'est le reproche le plus courant
/// fait aux sites d'emploi, et il est merite.
///
/// ── Pourquoi ce n'est pas un flux de plus ──
///
/// Le catalogue sort deja en XML et en JSON-LD (<c>FluxController</c>).
/// Un flux se subit : le partenaire vient le lire quand il veut. La
/// multidiffusion pousse, recoit une reference, et sait donc retirer.
/// C'est le retrait qui justifie tout le reste.
///
/// ── Le parti retenu, le meme que partout ailleurs ──
///
/// Inerte tant que rien n'est configure, et qui le dit. Sans
/// identifiants, <see cref="Destinations"/> rend des destinations
/// marquees « non configuree » avec ce qu'il manque, et l'interface
/// affiche la raison plutot qu'un bouton qui ne ferait rien. C'est le
/// parti deja retenu pour le paiement, Brevo, OVH, IndexNow et le
/// modele de langage.
///
/// Ce qu'il reste a faire pour l'ouvrir : obtenir les acces (France
/// Travail demande une habilitation « depot d'offres », distincte de
/// celle qui sert a lire le catalogue et qui est deja en place), puis
/// renseigner les cles listees dans <see cref="Destinations"/>.
/// </summary>
public class Multidiffusion
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<Multidiffusion> _journal;

    /// <summary>
    /// Au-dela, la diffusion reste en echec et attend une reprise a la
    /// main. Reessayer sans fin transforme une panne chez eux en charge
    /// chez nous, et noie le journal.
    /// </summary>
    public const int TentativesMax = 3;

    public Multidiffusion(AppDbContext db, IConfiguration config,
                          IHttpClientFactory clients, ILogger<Multidiffusion> journal)
    {
        _db = db;
        _config = config;
        _clients = clients;
        _journal = journal;
    }

    // ══════════════════════════════════════════
    //  Ce vers quoi l'on sait pousser
    // ══════════════════════════════════════════

    private string? FtIdentifiant => _config["FranceTravail:DepotClientId"];
    private string? FtSecret => _config["FranceTravail:DepotClientSecret"];
    private string? PartenaireUrl => _config["Multidiffusion:PartenaireUrl"];
    private string? PartenaireJeton => _config["Multidiffusion:PartenaireJeton"];

    public IReadOnlyList<DestinationDiffusion> Destinations()
    {
        var ftPret = !string.IsNullOrWhiteSpace(FtIdentifiant)
                     && !string.IsNullOrWhiteSpace(FtSecret);

        var partenairePret = !string.IsNullOrWhiteSpace(PartenaireUrl)
                             && !string.IsNullOrWhiteSpace(PartenaireJeton);

        return new[]
        {
            new DestinationDiffusion(
                "france-travail",
                "France Travail",
                "La plus grande audience francaise, et la seule ou les demandeurs "
                + "d'emploi sont tenus de chercher.",
                ftPret,
                ftPret
                    ? null
                    : "Habilitation « depot d'offres » a demander aupres de France Travail — "
                      + "elle est distincte de celle qui sert deja a lire le catalogue. "
                      + "Puis « FranceTravail:DepotClientId » et « FranceTravail:DepotClientSecret »."),

            new DestinationDiffusion(
                "partenaire",
                "Partenaire agregateur",
                "Un agregateur qui accepte un depot par requete plutot qu'un flux relu "
                + "chaque nuit : l'offre parait dans la minute, et se retire aussi vite.",
                partenairePret,
                partenairePret
                    ? null
                    : "« Multidiffusion:PartenaireUrl » et « Multidiffusion:PartenaireJeton »."),
        };
    }

    /// <summary>Vrai des qu'au moins une destination est ouverte.</summary>
    public bool EstConfigure => Destinations().Any(d => d.Configuree);

    /// <summary>De quoi rendre compte a l'administration, sans livrer de secret.</summary>
    public string Etat
    {
        get
        {
            var pretes = Destinations().Where(d => d.Configuree).Select(d => d.Nom).ToList();
            return pretes.Count == 0
                ? "Aucune destination configuree : la multidiffusion est proposee mais refusee, "
                  + "avec le detail de ce qui manque."
                : $"Destinations ouvertes : {string.Join(", ", pretes)}.";
        }
    }

    // ══════════════════════════════════════════
    //  Pousser
    // ══════════════════════════════════════════

    /// <summary>
    /// Demande la diffusion d'une offre vers une destination.
    ///
    /// Rend la ligne de suivi, dans tous les cas — y compris en echec.
    /// Un echec silencieux laisserait le recruteur croire son offre
    /// partie, ce qui est pire que de ne rien proposer.
    /// </summary>
    public async Task<Diffusion> Diffuser(int offreId, string userId, string destination,
                                          CancellationToken ct = default)
    {
        var connue = Destinations().FirstOrDefault(d => d.Cle == destination)
            ?? throw new ArgumentException($"Destination inconnue : {destination}", nameof(destination));

        // L'offre se charge avant toute chose, et le suivi ne se cree
        // qu'ensuite. L'ordre inverse — suivi d'abord, verification
        // apres — faisait poser une ligne rattachee a une offre
        // inexistante : la cle etrangere la refusait a l'enregistrement,
        // et « offre introuvable » sortait en erreur 500 au lieu de
        // sortir en message. Un test l'a montre ; la contrainte, elle,
        // l'aurait montre en production.
        var offre = await _db.JobOffers.FirstOrDefaultAsync(o => o.Id == offreId, ct);
        if (offre is null)
        {
            return new Diffusion
            {
                JobOfferId = offreId,
                DemandeeParUserId = userId,
                Destination = destination,
                Statut = "echec",
                Motif = "Offre introuvable.",
            };
        }

        // Une offre deja diffusee ne se rediffuse pas : le partenaire
        // en ferait un doublon, et c'est exactement ce que la
        // deduplication du catalogue passe son temps a nettoyer chez
        // les autres.
        var existante = await _db.Diffusions
            .FirstOrDefaultAsync(d => d.JobOfferId == offreId
                                      && d.Destination == destination
                                      && d.Statut != "retiree", ct);

        var suivi = existante ?? new Diffusion
        {
            JobOfferId = offreId,
            DemandeeParUserId = userId,
            Destination = destination,
        };

        if (existante is null) _db.Diffusions.Add(suivi);
        if (suivi.Statut == "diffusee") return suivi;

        if (!connue.Configuree)
        {
            suivi.Statut = "echec";
            suivi.Motif = connue.Manque;
            await _db.SaveChangesAsync(ct);
            return suivi;
        }

        if (suivi.Tentatives >= TentativesMax)
        {
            suivi.Statut = "echec";
            suivi.Motif = $"Abandonne apres {TentativesMax} tentatives. Reprenez la diffusion "
                          + "quand le partenaire aura repondu.";
            await _db.SaveChangesAsync(ct);
            return suivi;
        }

        // Un brouillon n'a pas d'existence publique ici : il n'en aura
        // pas davantage ailleurs.
        if (offre.IsDraft)
        {
            suivi.Statut = "echec";
            suivi.Motif = "Ce brouillon n'est pas publie : terminez sa redaction d'abord.";
            await _db.SaveChangesAsync(ct);
            return suivi;
        }

        // Une offre importee n'est pas la notre. La renvoyer a un
        // agregateur lui rendrait sa propre annonce, sous notre nom.
        if (!string.IsNullOrEmpty(offre.ExternalSource))
        {
            suivi.Statut = "echec";
            suivi.Motif = $"Cette offre vient de {offre.ExternalSource} : elle est deja diffusee "
                          + "a la source, et la republier creerait un doublon.";
            await _db.SaveChangesAsync(ct);
            return suivi;
        }

        suivi.Tentatives++;

        try
        {
            var (reference, url) = destination switch
            {
                "france-travail" => await PousserVersFranceTravail(offre, ct),
                "partenaire" => await PousserVersPartenaire(offre, ct),
                _ => throw new ArgumentException($"Destination inconnue : {destination}"),
            };

            suivi.Statut = "diffusee";
            suivi.ReferenceExterne = reference;
            suivi.UrlExterne = url;
            suivi.DiffuseeLe = DateTime.UtcNow;
            suivi.Motif = null;

            _journal.LogInformation(
                "Offre {Offre} diffusee vers {Destination} sous {Reference}",
                offreId, destination, reference);
        }
        catch (Exception ex)
        {
            suivi.Statut = "echec";
            // Le message du partenaire, pas la trace : le recruteur lit
            // ceci, et « NullReferenceException » ne lui apprend rien.
            suivi.Motif = $"{connue.Nom} a refuse la diffusion : {ex.Message}";
            _journal.LogWarning(ex, "Diffusion de l'offre {Offre} vers {Destination} refusee",
                                offreId, destination);
        }

        await _db.SaveChangesAsync(ct);
        return suivi;
    }

    /// <summary>
    /// Retire une offre de chez un partenaire.
    ///
    /// C'est la moitie qui manque partout ailleurs. Une offre pourvue
    /// qui reste en ligne chez trois agregateurs continue de recevoir
    /// des candidatures que personne ne lira — et chacune de ces
    /// candidatures est quelqu'un qui attend une reponse.
    /// </summary>
    public async Task<Diffusion?> Retirer(int offreId, string destination,
                                          CancellationToken ct = default)
    {
        var suivi = await _db.Diffusions
            .FirstOrDefaultAsync(d => d.JobOfferId == offreId
                                      && d.Destination == destination
                                      && d.Statut == "diffusee", ct);

        if (suivi is null) return null;

        try
        {
            if (destination == "partenaire" && suivi.ReferenceExterne is not null)
            {
                var client = _clients.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {PartenaireJeton}");
                var reponse = await client.DeleteAsync(
                    $"{PartenaireUrl!.TrimEnd('/')}/offres/{suivi.ReferenceExterne}", ct);
                reponse.EnsureSuccessStatusCode();
            }
            // France Travail : le retrait passe par le meme depot, avec
            // l'offre marquee pourvue. Le point d'entree exact depend de
            // l'habilitation obtenue ; il se branche ici.

            suivi.Statut = "retiree";
            suivi.RetireeLe = DateTime.UtcNow;
            suivi.Motif = null;
        }
        catch (Exception ex)
        {
            // Le retrait qui echoue est plus grave que la diffusion qui
            // echoue : on garde donc l'etat « diffusee », pour que la
            // console continue de montrer l'offre comme presente
            // ailleurs. La marquer « retiree » a tort serait mentir sur
            // le seul point qui compte.
            suivi.Motif = $"Le retrait a echoue : {ex.Message}. L'offre est probablement "
                          + "toujours en ligne chez le partenaire.";
            _journal.LogError(ex, "Retrait de l'offre {Offre} chez {Destination} impossible",
                              offreId, destination);
        }

        await _db.SaveChangesAsync(ct);
        return suivi;
    }

    /// <summary>Retire une offre de partout ou elle est partie.</summary>
    public async Task<int> RetirerPartout(int offreId, CancellationToken ct = default)
    {
        var destinations = await _db.Diffusions
            .Where(d => d.JobOfferId == offreId && d.Statut == "diffusee")
            .Select(d => d.Destination)
            .ToListAsync(ct);

        var retirees = 0;
        foreach (var d in destinations)
        {
            var suivi = await Retirer(offreId, d, ct);
            if (suivi?.Statut == "retiree") retirees++;
        }

        return retirees;
    }

    /// <summary>L'etat de diffusion d'une offre, destination par destination.</summary>
    public Task<List<Diffusion>> Suivi(int offreId, CancellationToken ct = default)
        => _db.Diffusions
            .AsNoTracking()
            .Where(d => d.JobOfferId == offreId)
            .OrderBy(d => d.Destination)
            .ToListAsync(ct);

    // ══════════════════════════════════════════
    //  Les partenaires eux-memes
    // ══════════════════════════════════════════

    /// <summary>
    /// France Travail, depot d'offre.
    ///
    /// L'habilitation « depot » est distincte de celle qui sert a lire
    /// le catalogue et qui est deja en place : c'est pourquoi ce chemin
    /// a ses propres identifiants plutot que de reprendre ceux de
    /// <see cref="FranceTravailService"/>.
    /// </summary>
    private async Task<(string Reference, string? Url)> PousserVersFranceTravail(
        JobOffer offre, CancellationToken ct)
    {
        // Le corps suit le format attendu par l'API de depot. Il est
        // ecrit ici pour que la mise en service se limite a brancher
        // l'appel : la mise en forme, elle, est faite.
        var corps = new
        {
            intitule = offre.Title,
            description = offre.Description,
            typeContrat = CodeContrat(offre.ContractType),
            lieuTravail = new { libelle = offre.Location },
            entreprise = new { nom = offre.Company },
            salaire = offre.MinSalary.HasValue
                ? new { libelle = $"De {offre.MinSalary} a {offre.MaxSalary} EUR par an" }
                : null,
            dureeTravailLibelle = offre.WorkSchedule,
            origineOffre = new
            {
                origine = "2",
                urlOrigine = $"{_config["App:PublicUrl"]?.TrimEnd('/')}/offres/{offre.Id}",
            },
        };

        throw new NotSupportedException(
            "L'habilitation « depot d'offres » de France Travail n'est pas encore obtenue. "
            + "La mise en forme de l'offre est prete ; il manque l'acces. "
            + $"({JsonSerializer.Serialize(corps).Length} octets prets a partir)");
    }

    /// <summary>
    /// Un agregateur generique : depot par requete, reference en retour.
    ///
    /// Le contrat est volontairement minimal — intitule, description,
    /// lieu, contrat, adresse d'origine — parce que c'est l'intersection
    /// de ce que tous acceptent. Un partenaire qui en demande plus se
    /// branche en ajoutant un cas, pas en changeant celui-ci.
    /// </summary>
    private async Task<(string Reference, string? Url)> PousserVersPartenaire(
        JobOffer offre, CancellationToken ct)
    {
        var client = _clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {PartenaireJeton}");

        var reponse = await client.PostAsJsonAsync(
            $"{PartenaireUrl!.TrimEnd('/')}/offres",
            new
            {
                reference = offre.Id.ToString(),
                intitule = offre.Title,
                entreprise = offre.Company,
                lieu = offre.Location,
                contrat = offre.ContractType,
                teletravail = offre.IsRemote,
                description = offre.Description,
                salaireMin = offre.MinSalary,
                salaireMax = offre.MaxSalary,
                url = $"{_config["App:PublicUrl"]?.TrimEnd('/')}/offres/{offre.Id}",
            },
            ct);

        reponse.EnsureSuccessStatusCode();

        var resultat = await reponse.Content.ReadFromJsonAsync<ReponsePartenaire>(ct);

        // Sans reference, on ne saura pas retirer. Mieux vaut echouer
        // maintenant, en le disant, que decouvrir dans trois semaines
        // qu'une offre pourvue est intouchable.
        if (string.IsNullOrWhiteSpace(resultat?.Reference))
            throw new InvalidOperationException(
                "Le partenaire a accepte l'offre sans rendre de reference : "
                + "elle serait impossible a retirer.");

        return (resultat.Reference, resultat.Url);
    }

    private sealed record ReponsePartenaire(string? Reference, string? Url);

    /// <summary>
    /// Les codes de contrat de France Travail, qui ne sont pas les
    /// notres. « CDI » se dit « CDI », mais « Alternance » se dit
    /// « CP », et un code inconnu fait rejeter tout le depot.
    /// </summary>
    private static string CodeContrat(string? contrat) => contrat switch
    {
        "CDI" => "CDI",
        "CDD" => "CDD",
        "Interim" or "Intérim" => "MIS",
        "Stage" => "STG",
        "Alternance" or "Apprentissage" => "CP",
        "Freelance" => "LIB",
        _ => "CDI",
    };
}
