using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// La seule porte vers les fichiers deposes.
///
/// Un CV n'appartient pas a la plateforme : il appartient a celui qui
/// l'a ecrit. Trois personnes seulement ont a le lire — son auteur, le
/// recruteur a qui il a ete adresse, et l'administrateur. Tout autre
/// recoit 404 : un 403 confirmerait que le fichier existe, et
/// permettrait de balayer les identifiants pour savoir qui a depose un
/// CV.
/// </summary>
[ApiController]
[Route("api/fichiers")]
[Authorize]
public class FichiersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly DepotFichiers _depot;
    private readonly PerimetreRecruteur _perimetre;
    private readonly ActivityLogService _log;

    public FichiersController(AppDbContext context, DepotFichiers depot,
                              PerimetreRecruteur perimetre, ActivityLogService log)
    {
        _context = context;
        _depot = depot;
        _perimetre = perimetre;
        _log = log;
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string UserFullName() => $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}";
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Sert un CV a qui a le droit de le lire.</summary>
    [HttpGet("cv/{nom}")]
    public async Task<IActionResult> Cv(string nom)
    {
        var moi = UserId();

        var fichier = DepotFichiers.Nom(nom);
        if (fichier == null) return NotFound();

        var chemin = DepotFichiers.Prefixe + fichier;
        var surDisque = _depot.Chemin(chemin);
        if (surDisque == null) return NotFound();

        // ── Qui peut lire ──
        var estAdmin = User.IsInRole("Admin");

        // Son propre CV : celui du profil, ou celui joint a l'une de ses
        // candidatures — les deux peuvent differer.
        var estLeSien = await _context.Users.AnyAsync(u => u.Id == moi && u.ResumeUrl == chemin)
                     || await _context.Applications.AnyAsync(a => a.UserId == moi && a.ResumeUrl == chemin);

        var autorise = estAdmin || estLeSien;

        if (!autorise)
        {
            // Le recruteur ne lit que ce qui lui a ete adresse : une
            // candidature portant ce CV, deposee sur une offre de son
            // entreprise. Un CV depose ailleurs lui reste ferme.
            var auteurs = await _context.Applications
                .Where(a => a.ResumeUrl == chemin)
                .Select(a => a.JobOffer.CreatedByUserId)
                .Distinct()
                .ToListAsync();

            foreach (var auteur in auteurs)
            {
                if (await _perimetre.PeutGerer(moi, auteur)) { autorise = true; break; }
            }
        }

        if (!autorise) return NotFound();

        // Lire le CV de quelqu'un depuis le panneau d'administration est
        // un acces aux donnees personnelles d'un tiers : il se trace.
        if (estAdmin && !estLeSien)
            await _log.Log("ReadResume", "User", null, $"CV consulté : {fichier}",
                           moi, UserFullName(), Ip());

        // « inline » : le navigateur l'affiche plutot que de le
        // telecharger, ce qui reste le geste attendu devant un CV.
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{fichier}\"";
        Response.Headers["Cache-Control"] = "private, no-store";
        return PhysicalFile(surDisque, "application/pdf");
    }
}
