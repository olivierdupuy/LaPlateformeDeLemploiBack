using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Le centre de preferences.
///
/// Se gere par jeton et non par compte. C'est le point : le lien arrive
/// au pied d'un courriel, et exiger une connexion pour cesser de
/// recevoir des courriels est exactement ce qui pousse quelqu'un vers le
/// bouton « indesirable » — lequel nous coute bien plus qu'un
/// desabonnement.
///
/// Un membre connecte peut aussi passer par ici sans jeton : c'est la
/// meme page, servie depuis son profil.
/// </summary>
[ApiController]
[Route("api/preferences-courriel")]
public class PreferencesCourrielController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ConsentementCourriel _consentement;
    private readonly UserManager<AppUser> _utilisateurs;

    public PreferencesCourrielController(
        AppDbContext context,
        ConsentementCourriel consentement,
        UserManager<AppUser> utilisateurs)
    {
        _context = context;
        _consentement = consentement;
        _utilisateurs = utilisateurs;
    }

    public class VuePreferences
    {
        public string Email { get; set; } = string.Empty;
        public bool AlertesOffres { get; set; }
        public bool SuiviCandidatures { get; set; }
        public bool Messages { get; set; }
        public bool Entretiens { get; set; }
        public bool LettreInformation { get; set; }
        public bool Actualites { get; set; }
        public bool ToutRefuse { get; set; }

        /// <summary>Ce qui partira toujours, dit explicitement pour ne pas laisser croire le contraire.</summary>
        public string[] Incontournables { get; set; } = Array.Empty<string>();
    }

    /// <summary>Lecture par jeton, sans compte.</summary>
    [HttpGet("{jeton}")]
    [AllowAnonymous]
    [EnableRateLimiting("identite")]
    public async Task<IActionResult> LireParJeton(string jeton)
    {
        var prefs = await _context.PreferencesCourriel
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Jeton == jeton);

        if (prefs is null)
            return NotFound(new { message = "Ce lien n'est plus valide. Demandez-en un nouveau depuis votre profil." });

        return Ok(Projeter(prefs));
    }

    /// <summary>Lecture pour un membre connecte.</summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> LireLesMiennes()
    {
        var email = await EmailDuMembre();
        if (email is null) return Unauthorized();

        var prefs = await _consentement.Obtenir(email);
        return Ok(Projeter(prefs));
    }

    public class MiseAJour
    {
        public bool AlertesOffres { get; set; }
        public bool SuiviCandidatures { get; set; }
        public bool Messages { get; set; }
        public bool Entretiens { get; set; }
        public bool LettreInformation { get; set; }
        public bool Actualites { get; set; }
        public bool ToutRefuse { get; set; }
    }

    [HttpPut("{jeton}")]
    [AllowAnonymous]
    [EnableRateLimiting("identite")]
    public async Task<IActionResult> EnregistrerParJeton(string jeton, [FromBody] MiseAJour m)
    {
        var prefs = await _context.PreferencesCourriel.FirstOrDefaultAsync(p => p.Jeton == jeton);
        if (prefs is null) return NotFound(new { message = "Ce lien n'est plus valide." });

        Appliquer(prefs, m);
        await _context.SaveChangesAsync();
        await SynchroniserLettre(prefs);

        return Ok(new { message = "Vos preferences sont enregistrees." });
    }

    [HttpPut]
    [Authorize]
    public async Task<IActionResult> EnregistrerLesMiennes([FromBody] MiseAJour m)
    {
        var email = await EmailDuMembre();
        if (email is null) return Unauthorized();

        var prefs = await _consentement.Obtenir(email);
        Appliquer(prefs, m);
        await _context.SaveChangesAsync();
        await SynchroniserLettre(prefs);

        return Ok(new { message = "Vos preferences sont enregistrees." });
    }

    // ── Retours d'expedition ──

    public class RetourEntrant
    {
        public string Email { get; set; } = string.Empty;
        /// <summary>« dur », « doux », « plainte ».</summary>
        public string Type { get; set; } = "dur";
        public string? Motif { get; set; }
    }

    /// <summary>
    /// Notification de rejet, appelee par Brevo.
    ///
    /// Protegee par un secret partage plutot que par un compte : c'est
    /// une machine qui appelle. Sans secret configure, la route est
    /// fermee — une porte ouverte permettrait a n'importe qui de faire
    /// bloquer l'adresse de son choix, ce qui reviendrait a en priver
    /// son titulaire.
    /// </summary>
    [HttpPost("retour")]
    [AllowAnonymous]
    public async Task<IActionResult> Retour(
        [FromBody] RetourEntrant retour,
        [FromHeader(Name = "X-Signature")] string? signature,
        [FromServices] IConfiguration config)
    {
        var attendu = config["Brevo:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(attendu)) return NotFound();
        if (signature != attendu) return Unauthorized();
        if (string.IsNullOrWhiteSpace(retour.Email)) return BadRequest();

        await _consentement.NoterRetour(retour.Email, retour.Type, retour.Motif);
        return NoContent();
    }

    /// <summary>Les adresses qu'on a cesse de servir, pour l'administration.</summary>
    [HttpGet("retours")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ListerRetours()
    {
        var lignes = await _context.RetoursCourriel
            .OrderByDescending(r => r.DernierLe)
            .Take(300)
            .ToListAsync();

        return Ok(lignes);
    }

    /// <summary>
    /// Rouvre une adresse bloquee.
    ///
    /// Le blocage se declenche sur un signal du prestataire, et ce
    /// signal se trompe. L'adresse est alors coupee de tout — y compris
    /// de la reinitialisation de mot de passe, qui est justement ce
    /// qu'on utilise quand on n'arrive plus a entrer. La liste montrait
    /// le probleme sans offrir le remede.
    /// </summary>
    [HttpPost("retours/{id:int}/debloquer")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Debloquer(int id, [FromServices] ActivityLogService activite)
    {
        var retour = await _context.RetoursCourriel.FindAsync(id);
        if (retour is null) return NotFound();

        var qui = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var fait = await _consentement.Debloquer(retour.Email, qui);
        if (!fait) return NotFound();

        // Rouvrir une adresse est une decision qui se trace : elle
        // remet en circulation des envois qu'un signal avait fait
        // cesser, et il faut pouvoir dire qui l'a prise.
        await activite.Log("adresse_debloquee", "RetourCourriel", id,
            "Adresse rouverte manuellement");

        return Ok(new { message = "Adresse rouverte. Les envois reprendront au prochain message." });
    }

    // ── Interne ──

    private async Task<string?> EmailDuMembre()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return null;
        var membre = await _utilisateurs.FindByIdAsync(id);
        return membre?.Email;
    }

    private static VuePreferences Projeter(PreferencesCourriel p) => new()
    {
        Email = p.Email,
        AlertesOffres = p.AlertesOffres,
        SuiviCandidatures = p.SuiviCandidatures,
        Messages = p.Messages,
        Entretiens = p.Entretiens,
        LettreInformation = p.LettreInformation,
        Actualites = p.Actualites,
        ToutRefuse = p.ToutRefuse,
        Incontournables = new[]
        {
            "Reinitialisation de mot de passe",
            "Confirmation d'adresse",
            "Alerte de connexion inhabituelle",
        },
    };

    private static void Appliquer(PreferencesCourriel p, MiseAJour m)
    {
        p.AlertesOffres = m.AlertesOffres;
        p.SuiviCandidatures = m.SuiviCandidatures;
        p.Messages = m.Messages;
        p.Entretiens = m.Entretiens;
        p.LettreInformation = m.LettreInformation;
        p.Actualites = m.Actualites;
        p.ToutRefuse = m.ToutRefuse;
        p.MisAJourLe = DateTime.UtcNow;
    }

    /// <summary>
    /// La lettre d'information a sa propre table d'abonnes, avec son
    /// double opt-in. Deux registres qui se contredisent finissent
    /// toujours par envoyer a quelqu'un qui a dit non : celui-ci fait
    /// foi, et l'autre le suit.
    /// </summary>
    private async Task SynchroniserLettre(PreferencesCourriel prefs)
    {
        var abonne = await _context.NewsletterSubscribers
            .FirstOrDefaultAsync(a => a.Email == prefs.Email);

        if (abonne is null) return;

        var veut = prefs.LettreInformation && !prefs.ToutRefuse;
        var abonneActuellement = abonne.Status != "Unsubscribed" && abonne.UnsubscribedAt is null;
        if (veut == abonneActuellement) return;

        if (veut)
        {
            // On ne re-confirme pas a sa place : le double opt-in a deja
            // eu lieu, et le desabonnement ne l'annule pas. Reactiver ici
            // rend simplement l'abonnement precedent.
            abonne.Status = abonne.ConfirmedAt is null ? "Pending" : "Confirmed";
            abonne.UnsubscribedAt = null;
            abonne.UnsubscribeReason = null;
        }
        else
        {
            abonne.Status = "Unsubscribed";
            abonne.UnsubscribedAt = DateTime.UtcNow;
            abonne.UnsubscribeReason = "Centre de preferences";
        }

        await _context.SaveChangesAsync();
    }
}
