using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Authentification par cle d'API.
///
/// Separee de l'authentification par jeton JWT et c'est deliberé : une
/// cle d'API n'ouvre pas une session, elle n'expire pas d'elle-meme, et
/// elle ne porte que les portees qu'on lui a donnees. Les melanger
/// aurait donne a une cle de lecture le droit de supprimer un compte.
/// </summary>
public sealed class CleApiAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _portee;

    public CleApiAttribute(string portee) => _portee = portee;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext contexte)
    {
        var entete = contexte.HttpContext.Request.Headers.Authorization.FirstOrDefault();
        var cle = entete?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? entete[7..].Trim()
            : null;

        if (string.IsNullOrWhiteSpace(cle))
        {
            contexte.Result = new UnauthorizedObjectResult(new
            {
                message = "Cle d'API absente. Passez-la dans l'en-tete Authorization : « Bearer lpde_… ».",
            });
            return;
        }

        var db = contexte.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var empreinte = IntegrationsController.Empreinte(cle);

        var jeton = await db.JetonsApi.FirstOrDefaultAsync(j => j.Empreinte == empreinte);

        if (jeton is null || jeton.RevoqueLe is not null)
        {
            contexte.Result = new UnauthorizedObjectResult(new
            {
                message = "Cle d'API inconnue ou revoquee.",
            });
            return;
        }

        if (!jeton.Portees.Split(',', StringSplitOptions.TrimEntries).Contains(_portee))
        {
            contexte.Result = new ObjectResult(new
            {
                message = $"Cette cle ne porte pas la portee « {_portee} ».",
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        // La date de derniere utilisation sert a reperer les cles
        // oubliees. Ecrite sans attendre la suite : si elle echoue, la
        // requete doit passer quand meme.
        jeton.DerniereUtilisation = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); } catch { /* sans consequence */ }

        contexte.HttpContext.Items["ApiUserId"] = jeton.UserId;
    }
}

/// <summary>
/// L'API publique, version 1.
///
/// Versionnee dans le chemin — « /api/v1/… » — parce qu'un partenaire
/// qui a branche son logiciel de recrutement chez nous ne redeploiera
/// pas le jour ou nous renommerons un champ. Sans numero de version,
/// toute evolution devient une rupture, et on finit par ne plus rien
/// changer.
///
/// Les reponses sont volontairement plates et stables : ce ne sont pas
/// les entites internes, ce sont des contrats. Ajouter un champ ici est
/// permis ; en retirer un ou le renommer demande une version 2.
/// </summary>
[ApiController]
[Route("api/v1")]
[AllowAnonymous]
[EnableRateLimiting("catalogue-api")]
public class ApiPubliqueController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly QualiteCatalogue _qualite;
    private readonly FacturationService _facturation;

    public ApiPubliqueController(AppDbContext context, QualiteCatalogue qualite,
                                 FacturationService facturation)
    {
        _context = context;
        _qualite = qualite;
        _facturation = facturation;
    }

    private string UtilisateurDeLaCle => (string)HttpContext.Items["ApiUserId"]!;

    /// <summary>Ce que l'API sait faire, pour qui la decouvre.</summary>
    [HttpGet]
    public IActionResult Racine() => Ok(new
    {
        version = "1",
        documentation = "https://www.laplateformedelemploi.com/guide/api",
        authentification = "En-tete « Authorization: Bearer <cle> ». Les cles se creent depuis l'espace recruteur.",
        points = new[]
        {
            "GET  /api/v1/offres — vos offres",
            "POST /api/v1/offres — publier une offre",
            "PATCH /api/v1/offres/{id} — modifier une offre",
            "DELETE /api/v1/offres/{id} — fermer une offre",
            "GET  /api/v1/candidatures — les candidatures recues",
            "PATCH /api/v1/candidatures/{id} — changer le statut d'une candidature",
        },
    });

    // ══════════════════════════════════════
    //  Offres
    // ══════════════════════════════════════

    [HttpGet("offres")]
    [CleApi("offres:lire")]
    public async Task<IActionResult> ListerOffres([FromQuery] int page = 1, [FromQuery] int taille = 50)
    {
        var userId = UtilisateurDeLaCle;
        taille = Math.Clamp(taille, 1, 100);

        var requete = _context.JobOffers.AsNoTracking().Where(o => o.CreatedByUserId == userId);
        var total = await requete.CountAsync();

        var offres = await requete
            .OrderByDescending(o => o.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * taille)
            .Take(taille)
            .Select(o => Projeter(o))
            .ToListAsync();

        return Ok(new { total, page, taille, offres });
    }

    public class OffreEntrante
    {
        public string Titre { get; set; } = string.Empty;
        public string Entreprise { get; set; } = string.Empty;
        public string Lieu { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TypeContrat { get; set; } = "CDI";
        public string? Categorie { get; set; }
        public int? SalaireMin { get; set; }
        public int? SalaireMax { get; set; }
        public bool Teletravail { get; set; }
        public string? Reference { get; set; }
    }

    [HttpPost("offres")]
    [CleApi("offres:ecrire")]
    public async Task<IActionResult> PublierOffre([FromBody] OffreEntrante entrante)
    {
        var userId = UtilisateurDeLaCle;

        if (string.IsNullOrWhiteSpace(entrante.Titre) || string.IsNullOrWhiteSpace(entrante.Entreprise))
            return BadRequest(new { message = "« titre » et « entreprise » sont obligatoires." });

        // Le quota de la formule s'applique ici comme ailleurs : sans
        // cela, l'API serait la porte de service qui contourne la
        // facturation.
        var (autorise, motif, _, _) = await _facturation.PeutPublier(userId);
        if (!autorise) return StatusCode(402, new { message = motif });

        var offre = new JobOffer
        {
            Title = entrante.Titre.Trim(),
            Company = entrante.Entreprise.Trim(),
            Location = entrante.Lieu?.Trim() ?? string.Empty,
            Description = entrante.Description ?? string.Empty,
            ContractType = entrante.TypeContrat,
            Category = entrante.Categorie ?? "Autre",
            MinSalary = entrante.SalaireMin,
            MaxSalary = entrante.SalaireMax,
            IsRemote = entrante.Teletravail,
            CreatedByUserId = userId,
            IsActive = true,
            // La reference du partenaire, gardee telle quelle : c'est
            // elle qui lui permet de rapprocher nos identifiants des
            // siens sans tenir une table de correspondance.
            ExternalId = entrante.Reference is null ? null : $"api:{entrante.Reference}",
        };

        offre.Empreinte = QualiteCatalogue.Empreinte(offre.Title, offre.Company, offre.Location);

        // Le meme filtre que pour les offres importees : une annonce
        // deposee par API n'est pas plus fiable qu'une autre.
        _qualite.Filtrer(offre);

        var geo = GeoUtils.Geocode(offre.Location);
        if (geo != null) { offre.Latitude = geo.Value.Lat; offre.Longitude = geo.Value.Lng; }

        _context.JobOffers.Add(offre);
        await _context.SaveChangesAsync();

        return Created($"/api/v1/offres/{offre.Id}", Projeter(offre));
    }

    [HttpPatch("offres/{id:int}")]
    [CleApi("offres:ecrire")]
    public async Task<IActionResult> ModifierOffre(int id, [FromBody] OffreEntrante entrante)
    {
        var offre = await _context.JobOffers
            .FirstOrDefaultAsync(o => o.Id == id && o.CreatedByUserId == UtilisateurDeLaCle);

        if (offre is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(entrante.Titre)) offre.Title = entrante.Titre.Trim();
        if (!string.IsNullOrWhiteSpace(entrante.Lieu)) offre.Location = entrante.Lieu.Trim();
        if (!string.IsNullOrWhiteSpace(entrante.Description)) offre.Description = entrante.Description;
        if (entrante.SalaireMin is not null) offre.MinSalary = entrante.SalaireMin;
        if (entrante.SalaireMax is not null) offre.MaxSalary = entrante.SalaireMax;

        offre.Empreinte = QualiteCatalogue.Empreinte(offre.Title, offre.Company, offre.Location);

        await _context.SaveChangesAsync();
        return Ok(Projeter(offre));
    }

    /// <summary>
    /// Ferme une offre. Ne la supprime pas : les candidatures deja
    /// recues doivent rester rattachees a quelque chose.
    /// </summary>
    [HttpDelete("offres/{id:int}")]
    [CleApi("offres:ecrire")]
    public async Task<IActionResult> FermerOffre(int id)
    {
        var offre = await _context.JobOffers
            .FirstOrDefaultAsync(o => o.Id == id && o.CreatedByUserId == UtilisateurDeLaCle);

        if (offre is null) return NotFound();

        offre.IsActive = false;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ══════════════════════════════════════
    //  Candidatures
    // ══════════════════════════════════════

    [HttpGet("candidatures")]
    [CleApi("candidatures:lire")]
    public async Task<IActionResult> ListerCandidatures(
        [FromQuery] int? offreId, [FromQuery] string? statut,
        [FromQuery] int page = 1, [FromQuery] int taille = 50)
    {
        var userId = UtilisateurDeLaCle;
        taille = Math.Clamp(taille, 1, 100);

        var requete = _context.Applications
            .AsNoTracking()
            .Where(c => c.JobOffer!.CreatedByUserId == userId);

        if (offreId is not null) requete = requete.Where(c => c.JobOfferId == offreId);
        if (!string.IsNullOrWhiteSpace(statut)) requete = requete.Where(c => c.Status == statut);

        var total = await requete.CountAsync();

        var lignes = await requete
            .OrderByDescending(c => c.AppliedAt)
            .Skip((Math.Max(1, page) - 1) * taille)
            .Take(taille)
            .Select(c => new
            {
                id = c.Id,
                offreId = c.JobOfferId,
                statut = c.Status,
                deposeeLe = c.AppliedAt,
                candidat = new { nom = c.FullName, email = c.Email, telephone = c.Phone, ville = c.City },
                lettre = c.CoverLetter,
                adequation = c.QualificationScore,
            })
            .ToListAsync();

        return Ok(new { total, page, taille, candidatures = lignes });
    }

    public class StatutEntrant
    {
        public string Statut { get; set; } = string.Empty;
    }

    [HttpPatch("candidatures/{id:int}")]
    [CleApi("candidatures:ecrire")]
    public async Task<IActionResult> ChangerStatut(int id, [FromBody] StatutEntrant entrant)
    {
        var permis = new[] { "Pending", "Reviewed", "Accepted", "Rejected" };
        if (!permis.Contains(entrant.Statut))
            return BadRequest(new { message = $"Statut inconnu. Valeurs admises : {string.Join(", ", permis)}." });

        var candidature = await _context.Applications
            .Include(c => c.JobOffer)
            .FirstOrDefaultAsync(c => c.Id == id && c.JobOffer!.CreatedByUserId == UtilisateurDeLaCle);

        if (candidature is null) return NotFound();

        candidature.Status = entrant.Statut;
        await _context.SaveChangesAsync();

        return Ok(new { id = candidature.Id, statut = candidature.Status });
    }

    /// <summary>
    /// La forme rendue aux partenaires. Ce n'est pas l'entite interne :
    /// c'est un contrat, et il ne bouge pas quand la base bouge.
    /// </summary>
    private static object Projeter(JobOffer o) => new
    {
        id = o.Id,
        titre = o.Title,
        entreprise = o.Company,
        lieu = o.Location,
        typeContrat = o.ContractType,
        categorie = o.Category,
        salaireMin = o.MinSalary,
        salaireMax = o.MaxSalary,
        teletravail = o.IsRemote,
        active = o.IsActive,
        brouillon = o.IsDraft,
        moderation = o.ModerationStatus,
        creeeLe = o.CreatedAt,
        url = $"/offres/{o.Id}",
        reference = o.ExternalId != null && o.ExternalId.StartsWith("api:") ? o.ExternalId[4..] : null,
    };
}
