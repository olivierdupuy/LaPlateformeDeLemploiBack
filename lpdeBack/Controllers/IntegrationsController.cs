using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Services;
using lpdeBack.Validation;

namespace lpdeBack.Controllers;

/// <summary>
/// Cles d'API et webhooks, geres par le recruteur lui-meme.
///
/// Sans cela, s'interfacer avec nous supposait de piloter un navigateur —
/// ce que certains font, et qui casse a chaque changement d'ecran.
///
/// La cle n'est jamais stockee : seule son empreinte l'est, comme un mot
/// de passe. Le porteur la voit une fois, a la creation. C'est
/// desagreable et c'est le prix : une base qui fuit ne doit pas livrer
/// des acces en clair.
/// </summary>
[ApiController]
[Route("api/integrations")]
[Authorize(Roles = "Recruiter,Admin")]
public class IntegrationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly FacturationService _facturation;
    private readonly ActivityLogService _activite;

    public IntegrationsController(AppDbContext context, FacturationService facturation,
                                  ActivityLogService activite)
    {
        _context = context;
        _facturation = facturation;
        _activite = activite;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Les portees qu'une cle peut porter.</summary>
    private static readonly Dictionary<string, string> Portees = new()
    {
        ["offres:lire"] = "Lire vos offres",
        ["offres:ecrire"] = "Publier et modifier vos offres",
        ["candidatures:lire"] = "Lire les candidatures recues",
        ["candidatures:ecrire"] = "Changer le statut des candidatures",
    };

    [HttpGet("portees")]
    public IActionResult ListerPortees() =>
        Ok(Portees.Select(p => new { cle = p.Key, libelle = p.Value }));

    [HttpGet("evenements")]
    public IActionResult ListerEvenements() =>
        Ok(WebhookService.Evenements.Select(e => new { cle = e.Key, libelle = e.Value }));

    // ══════════════════════════════════════
    //  Cles d'API
    // ══════════════════════════════════════

    [HttpGet("cles")]
    public async Task<IActionResult> ListerCles()
    {
        var cles = await _context.JetonsApi
            .AsNoTracking()
            .Where(j => j.UserId == UserId)
            .OrderByDescending(j => j.CreeLe)
            .Select(j => new
            {
                j.Id,
                j.Nom,
                j.Prefixe,
                j.Portees,
                j.CreeLe,
                j.DerniereUtilisation,
                j.RevoqueLe,
                revoquee = j.RevoqueLe != null,
            })
            .ToListAsync();

        return Ok(cles);
    }

    public class NouvelleCle
    {
        public string Nom { get; set; } = string.Empty;
        public string[] Portees { get; set; } = Array.Empty<string>();
    }

    [HttpPost("cles")]
    public async Task<IActionResult> CreerCle([FromBody] NouvelleCle demande)
    {
        // L'acces a l'API fait partie de la formule Pro : c'est
        // precisement ce qu'un recruteur equipe vient chercher, et le
        // reserver donne un contenu a la formule.
        var formule = await _facturation.FormuleDe(UserId);
        if (formule.Cle != "pro" && !User.IsInRole("Admin"))
            return StatusCode(403, new
            {
                message = "L'acces a l'API est inclus dans la formule Pro.",
            });

        if (string.IsNullOrWhiteSpace(demande.Nom))
            return BadRequest(new { message = "Donnez un nom a cette cle : sans lui, on ne sait plus laquelle revoquer." });

        var demandees = demande.Portees.Where(p => Portees.ContainsKey(p)).ToArray();
        if (demandees.Length == 0)
            return BadRequest(new { message = "Choisissez au moins une portee." });

        // « lpde_ » en tete : une cle qui fuit dans un depot public est
        // reconnaissable, donc detectable par les outils de balayage.
        var brute = "lpde_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(30))
            .Replace("+", "").Replace("/", "").Replace("=", "");

        var jeton = new JetonApi
        {
            UserId = UserId,
            Nom = demande.Nom.Trim(),
            Prefixe = brute[..13],
            Empreinte = Empreinte(brute),
            Portees = string.Join(',', demandees),
        };

        _context.JetonsApi.Add(jeton);
        await _context.SaveChangesAsync();
        await _activite.Log("cle_api_creee", "JetonApi", jeton.Id, jeton.Nom);

        return Ok(new
        {
            jeton.Id,
            jeton.Nom,
            jeton.Portees,
            cle = brute,
            message = "Copiez cette cle maintenant : elle ne sera plus jamais affichee.",
        });
    }

    [HttpDelete("cles/{id:int}")]
    public async Task<IActionResult> RevoquerCle(int id)
    {
        var jeton = await _context.JetonsApi.FirstOrDefaultAsync(j => j.Id == id && j.UserId == UserId);
        if (jeton is null) return NotFound();

        // Revoquee, pas effacee : on veut pouvoir repondre a « qui a
        // fait cet appel il y a trois semaines ».
        jeton.RevoqueLe = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _activite.Log("cle_api_revoquee", "JetonApi", jeton.Id, jeton.Nom);

        return NoContent();
    }

    // ══════════════════════════════════════
    //  Webhooks
    // ══════════════════════════════════════

    [HttpGet("webhooks")]
    public async Task<IActionResult> ListerWebhooks()
    {
        var abonnes = await _context.Webhooks
            .AsNoTracking()
            .Where(w => w.UserId == UserId)
            .OrderByDescending(w => w.CreeLe)
            .Select(w => new
            {
                w.Id,
                w.Url,
                w.Evenements,
                w.Actif,
                w.CreeLe,
                w.DerniereLivraison,
                w.DerniereErreur,
                w.EchecsConsecutifs,
            })
            .ToListAsync();

        return Ok(abonnes);
    }

    public class NouveauWebhook
    {
        public string Url { get; set; } = string.Empty;
        public string[] Evenements { get; set; } = Array.Empty<string>();
    }

    [HttpPost("webhooks")]
    public async Task<IActionResult> CreerWebhook([FromBody] NouveauWebhook demande)
    {
        var formule = await _facturation.FormuleDe(UserId);
        if (formule.Cle != "pro" && !User.IsInRole("Admin"))
            return StatusCode(403, new { message = "Les webhooks sont inclus dans la formule Pro." });

        // HTTPS seulement : la charge contient des noms de candidats et
        // des adresses. En clair, elle est lisible par tout le chemin.
        if (!Uri.TryCreate(demande.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            return BadRequest(new { message = "L'adresse doit etre une URL HTTPS." });

        var evenements = demande.Evenements.Where(e => WebhookService.Evenements.ContainsKey(e)).ToArray();
        if (evenements.Length == 0)
            return BadRequest(new { message = "Choisissez au moins un evenement." });

        var abonne = new Webhook
        {
            UserId = UserId,
            Url = demande.Url.Trim(),
            Evenements = string.Join(',', evenements),
            Secret = WebhookService.NouveauSecret(),
        };

        _context.Webhooks.Add(abonne);
        await _context.SaveChangesAsync();
        await _activite.Log("webhook_cree", "Webhook", abonne.Id, abonne.Url);

        return Ok(new
        {
            abonne.Id,
            abonne.Url,
            abonne.Evenements,
            secret = abonne.Secret,
            message = "Conservez ce secret : il sert a verifier la signature de chaque livraison, et ne sera plus affiche.",
        });
    }

    [HttpDelete("webhooks/{id:int}")]
    public async Task<IActionResult> SupprimerWebhook(int id)
    {
        var abonne = await _context.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.UserId == UserId);
        if (abonne is null) return NotFound();

        _context.Webhooks.Remove(abonne);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Les dernieres livraisons, pour repondre a « je n'ai rien recu ».</summary>
    [HttpGet("webhooks/{id:int}/livraisons")]
    public async Task<IActionResult> Livraisons(int id)
    {
        var sien = await _context.Webhooks.AnyAsync(w => w.Id == id && w.UserId == UserId);
        if (!sien) return NotFound();

        var lignes = await _context.LivraisonsWebhook
            .AsNoTracking()
            .Where(l => l.WebhookId == id)
            .OrderByDescending(l => l.CreeLe)
            .Take(50)
            .ToListAsync();

        return Ok(lignes);
    }

    // ═══════════════════════════════════
    //  Multidiffusion
    // ═══════════════════════════════════
    //
    // Un recruteur redepose son offre chez France Travail puis chez deux
    // agregateurs, a la main. Puis il pourvoit le poste et en oublie la
    // moitie : les candidatures continuent d'arriver pendant des
    // semaines sur un poste ferme, et chacune est quelqu'un qui attend
    // une reponse.

    /// <summary>Les partenaires, et ce qu'il manque a ceux qui ne sont pas prets.</summary>
    [HttpGet("diffusion/destinations")]
    public ActionResult<object> Destinations([FromServices] Multidiffusion diffusion)
        => Ok(new
        {
            configure = diffusion.EstConfigure,
            destinations = diffusion.Destinations(),
        });

    /// <summary>L'etat de diffusion d'une offre, destination par destination.</summary>
    [HttpGet("diffusion/{offreId:int}")]
    public async Task<ActionResult<object>> SuiviDiffusion(
        int offreId, [FromServices] Multidiffusion diffusion,
        [FromServices] PerimetreRecruteur perimetre)
    {
        if (!await PeutGererLOffre(offreId, perimetre)) return Forbid();
        return Ok(await diffusion.Suivi(offreId));
    }

    public class DemandeDiffusion
    {
        [Longueur(Limites.Nom)]
        public string Destination { get; set; } = string.Empty;
    }

    /// <summary>Pousse une offre chez un partenaire.</summary>
    [HttpPost("diffusion/{offreId:int}")]
    public async Task<ActionResult<object>> Diffuser(
        int offreId, [FromBody] DemandeDiffusion demande,
        [FromServices] Multidiffusion diffusion,
        [FromServices] PerimetreRecruteur perimetre)
    {
        if (!await PeutGererLOffre(offreId, perimetre)) return Forbid();

        Diffusion suivi;
        try
        {
            suivi = await diffusion.Diffuser(offreId, UserId, demande.Destination);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        await _activite.Log("DiffusionOffre", "JobOffer", offreId,
            $"Offre {offreId} vers {demande.Destination} : {suivi.Statut}",
            UserId, User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());

        // 200 meme en echec : l'echec est une reponse, pas une erreur de
        // requete. Le client affiche le motif tel quel — le recruteur
        // doit savoir pourquoi son offre n'est pas partie.
        return Ok(suivi);
    }

    /// <summary>Retire une offre de chez un partenaire, ou de partout.</summary>
    [HttpDelete("diffusion/{offreId:int}")]
    public async Task<ActionResult<object>> Retirer(
        int offreId, [FromQuery] string? destination,
        [FromServices] Multidiffusion diffusion,
        [FromServices] PerimetreRecruteur perimetre)
    {
        if (!await PeutGererLOffre(offreId, perimetre)) return Forbid();

        if (string.IsNullOrWhiteSpace(destination))
        {
            var retirees = await diffusion.RetirerPartout(offreId);
            await _activite.Log("RetraitDiffusion", "JobOffer", offreId,
                $"Offre {offreId} retiree de {retirees} partenaire(s)",
                UserId, User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
            return Ok(new { retirees });
        }

        var suivi = await diffusion.Retirer(offreId, destination);
        if (suivi is null)
            return NotFound(new { message = "Cette offre n'est pas diffusée chez ce partenaire." });

        await _activite.Log("RetraitDiffusion", "JobOffer", offreId,
            $"Offre {offreId} retirée de {destination} : {suivi.Statut}",
            UserId, User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(suivi);
    }

    /// <summary>
    /// Le perimetre s'applique ici comme ailleurs : diffuser l'offre
    /// d'une autre entreprise reviendrait a la publier sous son nom.
    /// </summary>
    private async Task<bool> PeutGererLOffre(int offreId, PerimetreRecruteur perimetre)
    {
        if (User.IsInRole("Admin")) return true;

        var auteur = await _context.JobOffers
            .Where(o => o.Id == offreId)
            .Select(o => o.CreatedByUserId)
            .FirstOrDefaultAsync();

        return auteur is not null && await perimetre.PeutGerer(UserId, auteur);
    }

    /// <summary>SHA-256, comme pour un mot de passe : la cle ne se retrouve pas depuis l'empreinte.</summary>
    public static string Empreinte(string cle) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cle))).ToLowerInvariant();
}
