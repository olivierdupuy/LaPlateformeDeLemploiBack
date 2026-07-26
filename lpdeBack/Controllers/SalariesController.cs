using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/salaries")]
public class SalariesController : ControllerBase
{
    private readonly AppDbContext _context;
    public SalariesController(AppDbContext context) => _context = context;

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Salaire annuel estimé d'une offre : milieu de fourchette, sinon la valeur disponible.
    private static int? AnnualOf(JobOffer j)
    {
        if (j.MinSalary.HasValue && j.MaxSalary.HasValue) return (j.MinSalary.Value + j.MaxSalary.Value) / 2;
        return j.MinSalary ?? j.MaxSalary;
    }

    /// <summary>Meilleurs salaires par métier (optionnellement filtrés par secteur / recherche).</summary>
    [HttpGet("roles")]
    public async Task<ActionResult<object>> GetRoles([FromQuery] string? sector, [FromQuery] string? q)
    {
        // Regroupement effectué côté SQL (agrégats) : on ne matérialise que le top 60,
        // pas les ~150k offres salariées.
        var query = _context.JobOffers
            .Where(j => j.IsActive && j.ModerationStatus == "Approved" && (j.MinSalary != null || j.MaxSalary != null));
        if (!string.IsNullOrWhiteSpace(sector))
            query = query.Where(j => j.Category == sector);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(j => j.Title.Contains(q));

        var raw = await query
            .GroupBy(j => j.Title)
            .Select(g => new
            {
                title = g.Key,
                category = g.Min(j => j.Category),
                avg = g.Average(j => ((double)(j.MinSalary ?? j.MaxSalary ?? 0) + (double)(j.MaxSalary ?? j.MinSalary ?? 0)) / 2),
                min = g.Min(j => j.MinSalary ?? j.MaxSalary ?? 0),
                max = g.Max(j => j.MaxSalary ?? j.MinSalary ?? 0),
                count = g.Count(),
            })
            .Where(r => r.avg > 0)
            .OrderByDescending(r => r.avg)
            .Take(60)
            .ToListAsync();

        var roles = raw.Select(r => new
        {
            title = r.title,
            category = r.category,
            avgAnnual = (int)Math.Round(r.avg),
            minAnnual = r.min,
            maxAnnual = r.max,
            count = r.count,
        });

        return new { roles };
    }

    /// <summary>Estimation détaillée pour un métier : fourchette, par lieu, par entreprise.</summary>
    [HttpGet("estimate")]
    public async Task<ActionResult<object>> GetEstimate([FromQuery] string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return BadRequest(new { message = "Titre requis." });
        var name = title.Trim();

        var offers = await _context.JobOffers
            .Where(j => j.IsActive && j.ModerationStatus == "Approved" && j.Title == name && (j.MinSalary != null || j.MaxSalary != null))
            .ToListAsync();
        var contributions = await _context.SalaryContributions
            .Where(c => c.JobTitle == name)
            .ToListAsync();

        var samples = offers.Select(AnnualOf).Where(v => v.HasValue).Select(v => v!.Value)
            .Concat(contributions.Select(c => c.AmountAnnual))
            .OrderBy(v => v)
            .ToList();

        if (samples.Count == 0)
            return new { title = name, count = 0, avgAnnual = 0, minAnnual = 0, medianAnnual = 0, maxAnnual = 0, byLocation = new object[0], byCompany = new object[0] };

        int Median(List<int> s) => s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;

        var byLocation = offers
            .Where(j => !string.IsNullOrWhiteSpace(j.Location))
            .GroupBy(j => j.Location.Trim())
            .Select(g => new { label = g.Key, avgAnnual = (int)g.Select(AnnualOf).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Average(), count = g.Count() })
            .Where(x => x.avgAnnual > 0)
            .OrderByDescending(x => x.avgAnnual).Take(8).ToList();

        var byCompany = offers
            .GroupBy(j => j.Company)
            .Select(g => new { label = g.Key, avgAnnual = (int)g.Select(AnnualOf).Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Average(), count = g.Count() })
            .Where(x => x.avgAnnual > 0)
            .OrderByDescending(x => x.avgAnnual).Take(8).ToList();

        return new
        {
            title = name,
            count = samples.Count,
            avgAnnual = (int)samples.Average(),
            minAnnual = samples.First(),
            medianAnnual = Median(samples),
            maxAnnual = samples.Last(),
            byLocation,
            byCompany,
        };
    }

    /// <summary>Partager un salaire (contribue aux estimations).</summary>
    [HttpPost("contribute")]
    [Authorize]
    public async Task<IActionResult> Contribute(SalaryContributionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.JobTitle) || dto.AmountAnnual < 1000)
            return BadRequest(new { message = "Intitulé de poste et salaire annuel valides requis." });

        _context.SalaryContributions.Add(new SalaryContribution
        {
            JobTitle = dto.JobTitle.Trim(),
            Company = dto.Company,
            Location = dto.Location,
            AmountAnnual = dto.AmountAnnual,
            ContractType = dto.ContractType,
            ExperienceLevel = dto.ExperienceLevel,
            AuthorUserId = GetUserId(),
        });
        await _context.SaveChangesAsync();
        return Ok(new { message = "Merci pour votre contribution." });
    }
}

public class SalaryContributionDto
{
    public string JobTitle { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? Location { get; set; }
    public int AmountAnnual { get; set; }
    public string? ContractType { get; set; }
    public string? ExperienceLevel { get; set; }
}
