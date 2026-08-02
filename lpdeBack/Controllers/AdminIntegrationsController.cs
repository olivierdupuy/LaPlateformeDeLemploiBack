using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Les acces techniques, vus de la plateforme entiere.
///
/// « IntegrationsController » existe deja et repond aux administrateurs —
/// mais il repond sur le compte de celui qui demande. Un administrateur y
/// voit ses propres cles, pas celles des autres. La consequence n'est pas
/// theorique : aujourd'hui, une cle d'API qui fuit ne peut etre revoquee
/// que par la personne qui l'a creee. Si elle ne repond pas, il n'existe
/// aucun geste depuis la console.
///
/// Ce controleur est la vue d'ensemble qui manquait. Il ne cree rien : on
/// ne fabrique pas une cle a la place de quelqu'un. Il montre, il revoque,
/// et il trace.
/// </summary>
[ApiController]
[Route("api/admin/integrations")]
[Authorize(Roles = "Admin")]
public class AdminIntegrationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ActivityLogService _log;

    public AdminIntegrationsController(AppDbContext context, ActivityLogService log)
    {
        _context = context;
        _log = log;
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private string NomComplet() =>
        $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}".Trim();
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Sans appel depuis ce delai, une cle est dormante.
    ///
    /// Elle n'est pas revoquee d'office : une integration saisonniere se
    /// tait plusieurs mois et redemarre. Mais une cle qui ne sert plus est
    /// une cle dont personne ne surveille la fuite, et c'est ce que
    /// l'ecran signale.
    /// </summary>
    public const int JoursAvantDormance = 90;

    // ══════════════════════════════════════
    //  Les cles d'API
    // ══════════════════════════════════════

    /// <summary>
    /// Toutes les cles, avec leur porteur.
    ///
    /// Le volume d'appels n'y figure pas, et c'est assume : rien ne le
    /// compte aujourd'hui. « JetonApi » porte une date de dernier usage,
    /// pas un compteur, et inventer un chiffre serait pire que de n'en
    /// donner aucun. La date de dernier appel et la dormance disent deja
    /// l'essentiel — ce qui sert, ce qui dort, ce qui a ete revoque.
    /// </summary>
    [HttpGet("cles")]
    public async Task<ActionResult<object>> Cles(CancellationToken ct)
    {
        var limite = DateTime.UtcNow.AddDays(-JoursAvantDormance);

        var cles = await _context.JetonsApi
            .AsNoTracking()
            .OrderByDescending(c => c.RevoqueLe == null)
            .ThenByDescending(c => c.DerniereUtilisation ?? c.CreeLe)
            .Join(_context.Users, c => c.UserId, u => u.Id,
                (c, u) => new
                {
                    c.Id, c.Nom, c.Prefixe, c.Portees, c.CreeLe,
                    c.DerniereUtilisation, c.RevoqueLe,
                    proprietaire = u.Email,
                    proprietaireNom = (u.FirstName + " " + u.LastName).Trim(),
                    entreprise = u.Company,
                    role = u.Role,
                })
            .ToListAsync(ct);

        var sortie = cles.Select(c => new
        {
            c.Id, c.Nom, c.Prefixe, c.CreeLe, c.DerniereUtilisation, c.RevoqueLe,
            c.proprietaire, c.proprietaireNom, c.entreprise, c.role,
            portees = c.Portees.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            revoquee = c.RevoqueLe != null,
            // Jamais appelee, ou plus depuis trois mois. Une cle creee la
            // semaine derniere et jamais utilisee n'est pas dormante : elle
            // vient d'etre posee.
            dormante = c.RevoqueLe == null
                       && (c.DerniereUtilisation ?? c.CreeLe) < limite,
            jamaisUtilisee = c.RevoqueLe == null && c.DerniereUtilisation == null,
        }).ToList();

        return Ok(new
        {
            cles = sortie,
            actives = sortie.Count(c => !c.revoquee),
            dormantes = sortie.Count(c => c.dormante),
            revoquees = sortie.Count(c => c.revoquee),
        });
    }

    /// <summary>
    /// Revoquer une cle.
    ///
    /// Elle est marquee, jamais supprimee. Les journaux du serveur la
    /// nomment par son prefixe : effacer la ligne rendrait illisible tout
    /// ce qui s'est passe avant, au moment precis ou l'on cherche a
    /// comprendre ce qu'une cle compromise a fait.
    /// </summary>
    [HttpDelete("cles/{id:int}")]
    public async Task<IActionResult> RevoquerCle(int id, CancellationToken ct)
    {
        var cle = await _context.JetonsApi.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cle == null) return NotFound();

        if (cle.RevoqueLe != null)
            return Ok(new { message = "Cette cle etait deja revoquee." });

        cle.RevoqueLe = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var porteur = await _context.Users
            .Where(u => u.Id == cle.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct);

        await _log.Log("RevocationCleApi", "JetonApi", id,
            $"Cle « {cle.Nom} » ({cle.Prefixe}…) de {porteur ?? "compte inconnu"} revoquee depuis la console",
            UserId(), NomComplet(), Ip());

        return Ok(new { message = "Cle revoquee. Les appels suivants recevront 401." });
    }

    // ══════════════════════════════════════
    //  Les webhooks
    // ══════════════════════════════════════

    /// <summary>
    /// Tous les abonnements aux evenements, et leur sante.
    ///
    /// Un webhook se desactive tout seul au bout de trop d'echecs
    /// consecutifs — c'est ce qui evite de frapper une adresse morte
    /// pendant des mois. Mais personne n'en etait averti : le partenaire
    /// cesse simplement de recevoir, et l'apprend le jour ou il s'en
    /// apercoit.
    /// </summary>
    [HttpGet("webhooks")]
    public async Task<ActionResult<object>> Webhooks(CancellationToken ct)
    {
        var abonnes = await _context.Webhooks
            .AsNoTracking()
            .OrderByDescending(w => w.EchecsConsecutifs)
            .ThenByDescending(w => w.DerniereLivraison ?? w.CreeLe)
            .Join(_context.Users, w => w.UserId, u => u.Id,
                (w, u) => new
                {
                    w.Id, w.Url, w.Evenements, w.Actif, w.EchecsConsecutifs,
                    w.CreeLe, w.DerniereLivraison, w.DerniereErreur,
                    proprietaire = u.Email,
                    entreprise = u.Company,
                })
            .ToListAsync(ct);

        var sortie = abonnes.Select(w => new
        {
            w.Id, w.Url, w.Actif, w.EchecsConsecutifs, w.CreeLe,
            w.DerniereLivraison, w.DerniereErreur, w.proprietaire, w.entreprise,
            evenements = w.Evenements.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            // Un abonnement eteint par la machine se distingue d'un
            // abonnement eteint par son porteur : le premier est une
            // panne, le second un choix.
            tombe = !w.Actif && w.EchecsConsecutifs > 0,
        }).ToList();

        return Ok(new
        {
            webhooks = sortie,
            actifs = sortie.Count(w => w.Actif),
            tombes = sortie.Count(w => w.tombe),
        });
    }

    /// <summary>
    /// Les dernieres livraisons d'un abonnement.
    ///
    /// Le point d'entree existait cote recruteur et n'etait appele par
    /// aucun ecran. C'est pourtant la seule reponse a « je n'ai rien
    /// recu » : le code renvoye par le serveur d'en face, et le nombre de
    /// tentatives.
    ///
    /// La charge utile n'est pas rendue : elle contient les donnees de la
    /// candidature — nom, adresse, parfois le contenu de la lettre. Un
    /// administrateur qui diagnostique une panne de livraison n'a pas
    /// besoin de les lire.
    /// </summary>
    [HttpGet("webhooks/{id:int}/livraisons")]
    public async Task<ActionResult<object>> Livraisons(int id, CancellationToken ct)
    {
        if (!await _context.Webhooks.AnyAsync(w => w.Id == id, ct)) return NotFound();

        return Ok(await _context.LivraisonsWebhook
            .AsNoTracking()
            .Where(l => l.WebhookId == id)
            .OrderByDescending(l => l.Id)
            .Take(40)
            .Select(l => new
            {
                l.Id, l.Evenement, l.CodeReponse, l.Erreur, l.Tentatives, l.CreeLe, l.LivreLe,
                livree = l.LivreLe != null,
            })
            .ToListAsync(ct));
    }

    /// <summary>
    /// Remettre en service un abonnement tombe.
    ///
    /// Le porteur a corrige son serveur et ne peut rien faire : le
    /// compteur d'echecs ne se remet a zero qu'a une livraison reussie,
    /// et plus aucune livraison ne part puisque l'abonnement est eteint.
    /// Sans ce geste, la seule issue est d'en creer un autre — et de
    /// perdre l'historique de celui-ci.
    /// </summary>
    [HttpPost("webhooks/{id:int}/reactiver")]
    public async Task<ActionResult<object>> Reactiver(int id, CancellationToken ct)
    {
        var w = await _context.Webhooks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w == null) return NotFound();

        w.Actif = true;
        w.EchecsConsecutifs = 0;
        w.DerniereErreur = null;
        await _context.SaveChangesAsync(ct);

        await _log.Log("ReactivationWebhook", "Webhook", id,
            $"Abonnement vers {w.Url} remis en service", UserId(), NomComplet(), Ip());

        return Ok(new { message = "Abonnement remis en service. La prochaine livraison partira." });
    }

    // ══════════════════════════════════════
    //  La multidiffusion
    // ══════════════════════════════════════

    /// <summary>
    /// Ce qui ne part pas chez les partenaires.
    ///
    /// Une diffusion en echec est un etat, pas une exception avalee :
    /// elle attend une reprise a la main, et sans ecran cette reprise
    /// n'arrive jamais. Le recruteur, lui, croit son offre partie.
    /// </summary>
    [HttpGet("diffusions")]
    public async Task<ActionResult<object>> Diffusions(CancellationToken ct)
    {
        var lignes = await _context.Diffusions
            .AsNoTracking()
            .Include(d => d.JobOffer)
            .Where(d => d.Statut == "echec" || d.Statut == "en_attente")
            .OrderByDescending(d => d.DemandeeLe)
            .Take(100)
            .Select(d => new
            {
                d.Id, d.JobOfferId, d.Destination, d.Statut, d.Motif,
                d.Tentatives, d.DemandeeLe,
                offre = d.JobOffer != null ? d.JobOffer.Title : null,
            })
            .ToListAsync(ct);

        // Par destination : une seule qui tombe se voit tout de suite,
        // alors qu'une liste d'offres melangees ne dit pas d'ou vient la
        // panne.
        var parDestination = lignes
            .GroupBy(d => d.Destination)
            .Select(g => new
            {
                destination = g.Key,
                enEchec = g.Count(x => x.Statut == "echec"),
                enAttente = g.Count(x => x.Statut == "en_attente"),
                dernierMotif = g.Where(x => x.Motif != null)
                                .OrderByDescending(x => x.DemandeeLe)
                                .Select(x => x.Motif).FirstOrDefault(),
            })
            .OrderByDescending(g => g.enEchec)
            .ToList();

        return Ok(new { lignes, parDestination });
    }
}
