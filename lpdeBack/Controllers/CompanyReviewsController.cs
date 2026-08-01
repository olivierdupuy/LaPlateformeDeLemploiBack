using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;
using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/companies")]
public class CompanyReviewsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<AppUser> _userManager;

    public CompanyReviewsController(AppDbContext context, UserManager<AppUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>Avis d'une entreprise + agrégats (note globale, par critère, répartition).</summary>
    [HttpGet("{company}/reviews")]
    public async Task<ActionResult<object>> GetReviews(string company)
    {
        var name = Uri.UnescapeDataString(company);
        var reviews = await _context.CompanyReviews
            .Where(r => r.Company == name && r.Status == "Approved")
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var count = reviews.Count;
        double Avg(Func<CompanyReview, int> sel) => count == 0 ? 0 : Math.Round(reviews.Average(sel), 1);

        var distribution = Enumerable.Range(1, 5)
            .ToDictionary(star => star, star => reviews.Count(r => r.OverallRating == star));

        return new
        {
            company = name,
            count,
            average = Avg(r => r.OverallRating),
            criteria = new
            {
                workLifeBalance = Avg(r => r.WorkLifeBalance),
                payBenefits = Avg(r => r.PayBenefits),
                jobSecurity = Avg(r => r.JobSecurity),
                management = Avg(r => r.Management),
                culture = Avg(r => r.Culture),
            },
            distribution,
            reviews = reviews.Select(r => new
            {
                r.Id, r.OverallRating, r.WorkLifeBalance, r.PayBenefits, r.JobSecurity, r.Management, r.Culture,
                r.Title, r.Body, r.JobTitle, r.Location, r.AuthorName, r.CreatedAt,
            }),
        };
    }

    /// <summary>Déposer un avis sur une entreprise.</summary>
    [HttpPost("{company}/reviews")]
    [Authorize]
    public async Task<IActionResult> CreateReview(string company, CompanyReviewCreateDto dto)
    {
        var name = Uri.UnescapeDataString(company);
        if (dto.OverallRating < 1 || dto.OverallRating > 5)
            return BadRequest(new { message = "La note globale doit être comprise entre 1 et 5." });
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { message = "Un titre est requis." });

        var userId = GetUserId();
        var user = userId != null ? await _userManager.FindByIdAsync(userId) : null;

        int Clamp(int v) => Math.Clamp(v == 0 ? dto.OverallRating : v, 1, 5);

        var review = new CompanyReview
        {
            Company = name,
            OverallRating = dto.OverallRating,
            WorkLifeBalance = Clamp(dto.WorkLifeBalance),
            PayBenefits = Clamp(dto.PayBenefits),
            JobSecurity = Clamp(dto.JobSecurity),
            Management = Clamp(dto.Management),
            Culture = Clamp(dto.Culture),
            Title = dto.Title,
            Body = dto.Body,
            JobTitle = dto.JobTitle,
            Location = dto.Location,
            AuthorUserId = userId,
            AuthorName = user != null ? $"{user.FirstName} {user.LastName.FirstOrDefault()}." : "Anonyme",
        };
        _context.CompanyReviews.Add(review);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Merci pour votre avis." });
    }

    /// <summary>Note moyenne d'une entreprise (léger, pour les cartes/listes).</summary>
    [HttpGet("{company}/rating")]
    public async Task<ActionResult<object>> GetRating(string company)
    {
        var name = Uri.UnescapeDataString(company);
        var reviews = _context.CompanyReviews.Where(r => r.Company == name && r.Status == "Approved");
        var count = await reviews.CountAsync();
        var average = count == 0 ? 0 : Math.Round(await reviews.AverageAsync(r => (double)r.OverallRating), 1);
        return new { average, count };
    }

    // ═══ Questions / Réponses ═══

    [HttpGet("{company}/questions")]
    public async Task<ActionResult<object>> GetQuestions(string company)
    {
        var name = Uri.UnescapeDataString(company);
        var questions = await _context.CompanyQuestions
            .Where(q => q.Company == name)
            .OrderByDescending(q => q.CreatedAt)
            .Select(q => new
            {
                q.Id, q.Body, q.AuthorName, q.CreatedAt,
                answers = q.Answers.OrderBy(a => a.CreatedAt)
                    .Select(a => new { a.Id, a.Body, a.AuthorName, a.CreatedAt }),
            })
            .ToListAsync();
        return Ok(questions);
    }

    [HttpPost("{company}/questions")]
    [Authorize]
    public async Task<IActionResult> AskQuestion(string company, CompanyTextDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Body)) return BadRequest(new { message = "Question vide." });
        var name = Uri.UnescapeDataString(company);
        var userId = GetUserId();
        var user = userId != null ? await _userManager.FindByIdAsync(userId) : null;
        _context.CompanyQuestions.Add(new CompanyQuestion
        {
            Company = name, Body = dto.Body.Trim(),
            AuthorUserId = userId,
            AuthorName = user != null ? $"{user.FirstName} {user.LastName.FirstOrDefault()}." : "Anonyme",
        });
        await _context.SaveChangesAsync();
        return Ok(new { message = "Question publiée." });
    }

    [HttpPost("questions/{questionId}/answers")]
    [Authorize]
    public async Task<IActionResult> AnswerQuestion(int questionId, CompanyTextDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Body)) return BadRequest(new { message = "Réponse vide." });
        var question = await _context.CompanyQuestions.FindAsync(questionId);
        if (question == null) return NotFound();
        var userId = GetUserId();
        var user = userId != null ? await _userManager.FindByIdAsync(userId) : null;
        _context.CompanyAnswers.Add(new CompanyAnswer
        {
            CompanyQuestionId = questionId, Body = dto.Body.Trim(),
            AuthorUserId = userId,
            AuthorName = user != null ? $"{user.FirstName} {user.LastName.FirstOrDefault()}." : "Anonyme",
        });
        await _context.SaveChangesAsync();
        return Ok(new { message = "Réponse publiée." });
    }

    // ═══ Suivre une entreprise ═══

    [HttpGet("{company}/follow")]
    public async Task<ActionResult<object>> GetFollow(string company)
    {
        var name = Uri.UnescapeDataString(company);
        var userId = GetUserId();
        var count = await _context.CompanyFollows.CountAsync(f => f.Company == name);
        var following = userId != null && await _context.CompanyFollows.AnyAsync(f => f.Company == name && f.UserId == userId);
        return new { following, count };
    }

    [HttpPost("{company}/follow")]
    [Authorize]
    public async Task<ActionResult<object>> ToggleFollow(string company)
    {
        var name = Uri.UnescapeDataString(company);
        var userId = GetUserId()!;
        var existing = await _context.CompanyFollows.FirstOrDefaultAsync(f => f.Company == name && f.UserId == userId);
        if (existing != null) _context.CompanyFollows.Remove(existing);
        else _context.CompanyFollows.Add(new CompanyFollow { Company = name, UserId = userId });
        await _context.SaveChangesAsync();
        var count = await _context.CompanyFollows.CountAsync(f => f.Company == name);
        return new { following = existing == null, count };
    }

    /// <summary>Activité d'une entreprise : recrutements récents + réactivité (public).</summary>
    [HttpGet("{company}/activity")]
    public async Task<ActionResult<object>> GetActivity(string company)
    {
        var name = Uri.UnescapeDataString(company);
        var thirty = DateTime.UtcNow.AddDays(-30);
        var sixty = DateTime.UtcNow.AddDays(-60);

        var offerIds = await _context.JobOffers.Where(j => j.Company == name).Select(j => j.Id).ToListAsync();
        if (offerIds.Count == 0) return new { hires30d = 0, responsive = false };

        var hires30d = await _context.Applications
            .CountAsync(a => offerIds.Contains(a.JobOfferId) && a.Status == "Accepted" && a.AppliedAt >= thirty);

        var recent = await _context.Applications
            .Where(a => offerIds.Contains(a.JobOfferId) && a.AppliedAt >= sixty)
            .Select(a => a.Status).ToListAsync();
        var responsive = recent.Count >= 3 && recent.Count(s => s != "Pending") >= recent.Count * 0.5;

        return new { hires30d, responsive };
    }

    // ═══ Fiche « À propos » ═══

    [HttpGet("{company}/profile")]
    public async Task<ActionResult<object>> GetProfile(string company)
    {
        var name = Uri.UnescapeDataString(company);
        var p = await _context.CompanyProfiles.FirstOrDefaultAsync(x => x.Company == name);
        var jobCount = await _context.JobOffers.CountAsync(j => j.Company == name && j.IsActive && j.ModerationStatus == "Approved");
        return new
        {
            company = name,
            foundedYear = p?.FoundedYear,
            size = p?.Size,
            industry = p?.Industry,
            headquarters = p?.Headquarters,
            website = p?.Website,
            about = p?.About,
            jobCount,
        };
    }

    [HttpPut("{company}/profile")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> UpsertProfile(string company, CompanyProfileDto dto)
    {
        var name = Uri.UnescapeDataString(company);
        var p = await _context.CompanyProfiles.FirstOrDefaultAsync(x => x.Company == name);
        if (p == null)
        {
            p = new CompanyProfile { Company = name };
            _context.CompanyProfiles.Add(p);
        }
        p.FoundedYear = dto.FoundedYear;
        p.Size = dto.Size;
        p.Industry = dto.Industry;
        p.Headquarters = dto.Headquarters;
        p.Website = dto.Website;
        p.About = dto.About;
        p.UpdatedByUserId = GetUserId();
        p.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Fiche entreprise enregistrée." });
    }

    /// <summary>Lieux où l'entreprise recrute (avec compteur d'offres).</summary>
    [HttpGet("{company}/locations")]
    public async Task<ActionResult<IEnumerable<object>>> GetLocations(string company)
    {
        var name = Uri.UnescapeDataString(company);
        return Ok(await _context.JobOffers
            .Where(j => j.Company == name && j.IsActive && j.ModerationStatus == "Approved" && j.Location != "")
            .GroupBy(j => j.Location)
            .Select(g => new { location = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync());
    }

    /// <summary>Salaires par poste dans l'entreprise (moyenne annuelle estimée).</summary>
    [HttpGet("{company}/salaries")]
    public async Task<ActionResult<IEnumerable<object>>> GetCompanySalaries(string company)
    {
        var name = Uri.UnescapeDataString(company);
        var offers = await _context.JobOffers
            .Where(j => j.Company == name && (j.MinSalary != null || j.MaxSalary != null))
            .ToListAsync();

        var roles = offers
            .GroupBy(j => j.Title.Trim())
            .Select(g =>
            {
                var vals = g.Select(j => j.MinSalary.HasValue && j.MaxSalary.HasValue
                        ? (j.MinSalary!.Value + j.MaxSalary!.Value) / 2
                        : (j.MinSalary ?? j.MaxSalary ?? 0))
                    .Where(v => v > 0).ToList();
                return new { title = g.Key, avgAnnual = vals.Count > 0 ? (int)vals.Average() : 0, count = g.Count() };
            })
            .Where(r => r.avgAnnual > 0)
            .OrderByDescending(r => r.avgAnnual)
            .Take(10)
            .ToList();
        return Ok(roles);
    }

    /// <summary>Autres entreprises du même secteur qui pourraient intéresser.</summary>
    [HttpGet("{company}/similar")]
    public async Task<ActionResult<IEnumerable<object>>> GetSimilar(string company)
    {
        var name = Uri.UnescapeDataString(company);
        var cats = await _context.JobOffers.Where(j => j.Company == name).Select(j => j.Category).Distinct().ToListAsync();
        return Ok(await _context.JobOffers
            .Where(j => j.Company != name && j.IsActive && j.ModerationStatus == "Approved" && cats.Contains(j.Category))
            .GroupBy(j => j.Company)
            .Select(g => new { company = g.Key, jobCount = g.Count(), location = g.Select(j => j.Location).FirstOrDefault() })
            .OrderByDescending(x => x.jobCount)
            .Take(6)
            .ToListAsync());
    }

    // ═══ Modération des avis (admin) ═══

    [HttpGet("reviews/all")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<object>>> GetAllReviews([FromQuery] string? status)
    {
        var query = _context.CompanyReviews.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        return Ok(await query.OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.Company, r.OverallRating, r.Title, r.Body, r.JobTitle, r.Location, r.AuthorName, r.Status, r.CreatedAt })
            .ToListAsync());
    }

    [HttpPatch("reviews/{id}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetReviewStatus(int id, CompanyTextDto dto)
    {
        var review = await _context.CompanyReviews.FindAsync(id);
        if (review == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(dto.Body)) review.Status = dto.Body;
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

public class CompanyTextDto
{
    [Required(ErrorMessage = "Écrivez votre message.")]
    [StringLength(Limites.Texte, MinimumLength = 2, ErrorMessage = "Le texte fait entre 2 et 20 000 caractères.")]
    public string Body { get; set; } = string.Empty;
}

public class CompanyProfileDto
{
    // Aucune entreprise n'a ete fondee en l'an 300, ni en 2400. La borne
    // haute est calculee : une annee future n'est pas une fondation.
    [Range(1800, 2100, ErrorMessage = "L'année de création doit être comprise entre 1800 et 2100.")]
    public int? FoundedYear { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Size { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Industry { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Headquarters { get; set; }

    [AdresseWeb]
    public string? Website { get; set; }

    [StringLength(Limites.Texte, ErrorMessage = "La présentation ne peut pas dépasser 20 000 caractères.")]
    public string? About { get; set; }
}

public class CompanyReviewCreateDto
{
    // Les six notes sont sur cinq. Sans borne, une note de 10 000 sur une
    // seule ligne ecrase la moyenne de l'entreprise entiere — et la fiche
    // publique affiche alors un chiffre que personne ne peut expliquer.
    [Range(1, 5, ErrorMessage = "La note globale va de 1 à 5.")]
    public int OverallRating { get; set; }

    [Range(1, 5, ErrorMessage = "La note « équilibre » va de 1 à 5.")]
    public int WorkLifeBalance { get; set; }

    [Range(1, 5, ErrorMessage = "La note « rémunération » va de 1 à 5.")]
    public int PayBenefits { get; set; }

    [Range(1, 5, ErrorMessage = "La note « sécurité de l'emploi » va de 1 à 5.")]
    public int JobSecurity { get; set; }

    [Range(1, 5, ErrorMessage = "La note « management » va de 1 à 5.")]
    public int Management { get; set; }

    [Range(1, 5, ErrorMessage = "La note « culture » va de 1 à 5.")]
    public int Culture { get; set; }

    [Required(ErrorMessage = "Donnez un titre à votre avis.")]
    [StringLength(Limites.Ligne, MinimumLength = 3, ErrorMessage = "Le titre fait entre 3 et 200 caractères.")]
    [SansBalisage]
    public string Title { get; set; } = string.Empty;

    [StringLength(Limites.Texte, ErrorMessage = "L'avis ne peut pas dépasser 20 000 caractères.")]
    public string? Body { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? JobTitle { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Location { get; set; }
}
