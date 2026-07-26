using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;

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
}

public class CompanyTextDto
{
    public string Body { get; set; } = string.Empty;
}

public class CompanyReviewCreateDto
{
    public int OverallRating { get; set; }
    public int WorkLifeBalance { get; set; }
    public int PayBenefits { get; set; }
    public int JobSecurity { get; set; }
    public int Management { get; set; }
    public int Culture { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? JobTitle { get; set; }
    public string? Location { get; set; }
}
