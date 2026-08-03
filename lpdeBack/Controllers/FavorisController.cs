using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Controllers;

/// <summary>
/// Les offres mises de cote.
///
/// Elles vivaient dans le stockage local de chaque client : le site et le
/// telephone en tenaient chacun une liste, sous la meme clef, sans jamais
/// se rejoindre. Mettre une offre de cote au bureau ne la faisait pas
/// apparaitre dans le train, et vider son navigateur les effacait toutes.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FavorisController : ControllerBase
{
    // Vingt recherches sauvegardees, deux cents favoris : mettre une offre
    // de cote ne demande aucun effort, la borne est donc plus haute. Elle
    // existe pour qu'une boucle fautive dans un client ne remplisse pas la
    // table, pas pour rationner qui que ce soit.
    private const int Plafond = 200;

    private readonly AppDbContext _context;
    public FavorisController(AppDbContext context) => _context = context;
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Les offres mises de cote, la plus recente d'abord.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> Lister()
    {
        var userId = UserId;
        var favoris = await _context.Favoris
            .Where(f => f.UserId == userId)
            .Include(f => f.JobOffer)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new
            {
                f.Id,
                f.JobOfferId,
                f.CreatedAt,
                offre = new
                {
                    f.JobOffer.Id,
                    f.JobOffer.Title,
                    f.JobOffer.Company,
                    f.JobOffer.Location,
                    f.JobOffer.ContractType,
                    f.JobOffer.IsRemote,
                    f.JobOffer.IsActive,
                    f.JobOffer.CreatedAt,
                },
            })
            .ToListAsync();

        return Ok(favoris);
    }

    /// <summary>
    /// Les seuls identifiants, pour que le client sache quoi cocher sans
    /// rapatrier les offres entieres a chaque affichage de liste.
    /// </summary>
    [HttpGet("ids")]
    public async Task<ActionResult<IEnumerable<int>>> Identifiants()
    {
        var userId = UserId;
        return Ok(await _context.Favoris
            .Where(f => f.UserId == userId)
            .Select(f => f.JobOfferId)
            .ToListAsync());
    }

    /// <summary>
    /// Met une offre de cote. Le geste est idempotent : le repeter rend le
    /// favori existant plutot qu'une erreur, parce que deux appareils qui
    /// se synchronisent peuvent tres bien l'envoyer tous les deux.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult> Ajouter(FavoriCreateDto dto)
    {
        var userId = UserId;

        var offreExiste = await _context.JobOffers.AnyAsync(j => j.Id == dto.JobOfferId);
        if (!offreExiste) return NotFound(new { message = "Cette offre n'existe pas ou plus." });

        var deja = await _context.Favoris
            .FirstOrDefaultAsync(f => f.UserId == userId && f.JobOfferId == dto.JobOfferId);
        if (deja != null) return Ok(new { deja.Id, deja.JobOfferId, deja.CreatedAt });

        if (await _context.Favoris.CountAsync(f => f.UserId == userId) >= Plafond)
            return BadRequest(new { message = $"Vous avez atteint {Plafond} favoris. Retirez-en pour en ajouter." });

        var favori = new Favori { UserId = userId, JobOfferId = dto.JobOfferId };
        _context.Favoris.Add(favori);
        await _context.SaveChangesAsync();

        return Ok(new { favori.Id, favori.JobOfferId, favori.CreatedAt });
    }

    /// <summary>
    /// Retire une offre, designee par l'offre elle-meme et non par le
    /// favori : c'est ce que le client connait quand il affiche un signet.
    /// Retirer ce qui n'y est plus reussit — le resultat voulu est atteint.
    /// </summary>
    [HttpDelete("{jobOfferId:int}")]
    public async Task<IActionResult> Retirer(int jobOfferId)
    {
        var userId = UserId;
        var favori = await _context.Favoris
            .FirstOrDefaultAsync(f => f.UserId == userId && f.JobOfferId == jobOfferId);

        if (favori != null)
        {
            _context.Favoris.Remove(favori);
            await _context.SaveChangesAsync();
        }
        return NoContent();
    }

    /// <summary>
    /// Verse d'un coup les favoris gardes jusqu'ici dans le navigateur ou
    /// le telephone. Sans cette reprise, activer la synchronisation aurait
    /// commence par effacer ce que les gens avaient deja mis de cote.
    /// </summary>
    [HttpPost("reprise")]
    public async Task<ActionResult> Reprendre(FavorisRepriseDto dto)
    {
        var userId = UserId;
        var demandes = (dto.JobOfferIds ?? new List<int>()).Distinct().ToList();
        if (demandes.Count == 0) return Ok(new { ajoutes = 0, ignores = 0 });

        var existants = await _context.Favoris
            .Where(f => f.UserId == userId)
            .Select(f => f.JobOfferId)
            .ToListAsync();

        var offresValides = await _context.JobOffers
            .Where(j => demandes.Contains(j.Id))
            .Select(j => j.Id)
            .ToListAsync();

        var place = await _context.Favoris.CountAsync(f => f.UserId == userId);
        var aAjouter = offresValides
            .Except(existants)
            .Take(Math.Max(0, Plafond - place))
            .ToList();

        foreach (var id in aAjouter)
            _context.Favoris.Add(new Favori { UserId = userId, JobOfferId = id });

        if (aAjouter.Count > 0) await _context.SaveChangesAsync();

        return Ok(new { ajoutes = aAjouter.Count, ignores = demandes.Count - aAjouter.Count });
    }
}

public class FavoriCreateDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Offre inconnue.")]
    public int JobOfferId { get; set; }
}

public class FavorisRepriseDto
{
    /// <summary>Au plus le plafond : au-dela, c'est une boucle, pas un choix.</summary>
    [MaxLength(500)]
    public List<int>? JobOfferIds { get; set; }
}
