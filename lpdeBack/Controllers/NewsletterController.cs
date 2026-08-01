using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Services;
using lpdeBack.Validation;

namespace lpdeBack.Controllers;

/// <summary>
/// La lettre d'information, cote public.
///
/// Tout y est ouvert sans compte, et ce n'est pas un oubli : quelqu'un
/// qui veut se desabonner n'a pas a se connecter d'abord, ni a retrouver
/// un mot de passe qu'il a peut-etre oublie depuis longtemps. Un lien,
/// un clic, c'est fini. La loi l'exige, et le bon sens aussi — le seul
/// autre geste possible pour cette personne serait de nous signaler comme
/// pourriel.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class NewsletterController : ControllerBase
{
    private readonly NewsletterService _lettre;
    private readonly AppDbContext _context;

    public NewsletterController(NewsletterService lettre, AppDbContext context)
    {
        _lettre = lettre;
        _context = context;
    }

    /// <summary>S'abonner. Rien ne part avant confirmation.</summary>
    [HttpPost("abonner")]
    public async Task<ActionResult<object>> Abonner([FromBody] AbonnementDto dto, CancellationToken ct)
    {
        // Un visiteur connecte n'a pas a ressaisir ce qu'on sait deja.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? prenom = dto.Prenom, nom = dto.Nom, ville = dto.Ville;

        if (!string.IsNullOrEmpty(userId))
        {
            var u = await _context.Users
                .Where(x => x.Id == userId)
                .Select(x => new { x.FirstName, x.LastName, x.City })
                .FirstOrDefaultAsync(ct);
            if (u != null)
            {
                prenom ??= u.FirstName;
                nom ??= u.LastName;
                ville ??= u.City;
            }
        }

        var r = await _lettre.Abonner(dto.Email ?? "", prenom, nom,
                                      dto.Source ?? "Footer", SessionService.Ip(HttpContext),
                                      userId, dto.Categories, ville, ct);

        return r.Ok ? Ok(new { message = r.Message }) : BadRequest(new { message = r.Message });
    }

    /// <summary>Le clic sur le lien de confirmation.</summary>
    [HttpPost("confirmer")]
    public async Task<ActionResult<object>> Confirmer([FromBody] JetonDto dto, CancellationToken ct)
    {
        var r = await _lettre.Confirmer(dto.Jeton ?? "", ct);
        return r.Ok ? Ok(new { message = r.Message }) : BadRequest(new { message = r.Message });
    }

    /// <summary>
    /// Se desabonner. En GET aussi, parce que les messageries qui
    /// implementent « List-Unsubscribe » appellent l'adresse elles-memes,
    /// sans que personne ne clique.
    /// </summary>
    [HttpPost("desinscription")]
    public async Task<ActionResult<object>> Desabonner([FromBody] DesinscriptionDto dto, CancellationToken ct)
    {
        var r = await _lettre.Desabonner(dto.Jeton ?? "", dto.Motif, ct);
        return r.Ok ? Ok(new { message = r.Message }) : BadRequest(new { message = r.Message });
    }

    /// <summary>
    /// Le desabonnement en un clic, appele par Gmail et Outlook eux-memes
    /// via l'en-tete « List-Unsubscribe-Post ». Sans cette route, le bouton
    /// que ces messageries affichent a cote de l'expediteur ne ferait rien.
    /// </summary>
    [HttpPost("desinscription/{jeton}")]
    public async Task<IActionResult> DesabonnerEnUnClic(string jeton, CancellationToken ct)
    {
        await _lettre.Desabonner(jeton, "Un clic depuis la messagerie", ct);
        return Ok();
    }

    /// <summary>L'etat d'une adresse, pour que la page sache quoi afficher.</summary>
    [HttpGet("etat")]
    public async Task<ActionResult<object>> Etat([FromQuery] string? jeton, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jeton)) return Ok(new { connu = false });

        var a = await _context.NewsletterSubscribers
            .Where(s => s.UnsubscribeToken == jeton)
            .Select(s => new { s.Email, s.Status, s.UnsubscribedAt })
            .FirstOrDefaultAsync(ct);

        if (a == null) return Ok(new { connu = false });

        // L'adresse est renvoyee partiellement masquee : le lien peut avoir
        // ete transfere, et rien n'oblige a montrer l'adresse entiere pour
        // que son proprietaire se reconnaisse.
        var arobase = a.Email.IndexOf('@');
        var masquee = arobase > 1
            ? $"{a.Email[..Math.Min(2, arobase)]}{new string('•', Math.Max(1, arobase - 2))}{a.Email[arobase..]}"
            : a.Email;

        return Ok(new
        {
            connu = true,
            email = masquee,
            desabonne = a.UnsubscribedAt != null,
            confirme = a.Status == "Confirmed",
        });
    }
}

public class AbonnementDto
{
    [Required(ErrorMessage = "Indiquez votre adresse e-mail.")]
    [AdresseCourriel]
    public string? Email { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Prenom { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Nom { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Ville { get; set; }

    [Longueur(Limites.Url), SansBalisage]
    public string? Categories { get; set; }

    /// <summary>
    /// D'ou vient l'abonnement : Footer, Page, Inscription.
    ///
    /// Enumere plutot que libre : cette valeur sert de preuve de
    /// consentement au sens du RGPD — elle dit ou la personne a coche.
    /// Une valeur inventee la rendrait inexploitable le jour ou il faut
    /// justifier l'envoi.
    /// </summary>
    [Parmi("Footer", "Page", "Inscription", "Import", "Admin")]
    public string? Source { get; set; }
}

public class JetonDto
{
    [Required, Longueur(200)]
    public string? Jeton { get; set; }
}

public class DesinscriptionDto
{
    [Required, Longueur(200)]
    public string? Jeton { get; set; }

    [Longueur(Limites.Paragraphe)]
    public string? Motif { get; set; }
}
