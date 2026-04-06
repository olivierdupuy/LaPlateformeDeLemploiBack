using System.Security.Claims;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.DTOs;
using LaPlateformeDeLemploiAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LaPlateformeDeLemploiAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecommendationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public RecommendationsController(AppDbContext context) => _context = context;

    // GET/POST preferences
    [HttpGet("preferences")]
    public async Task<ActionResult<PreferencesDto>> GetPreferences()
    {
        var prefs = await _context.JobPreferences.FirstOrDefaultAsync(p => p.UserId == UserId);
        if (prefs == null) return Ok(new PreferencesDto());

        return Ok(new PreferencesDto
        {
            DesiredContractTypes = Split(prefs.DesiredContractTypes),
            DesiredLocations = Split(prefs.DesiredLocations),
            DesiredCategoryIds = Split(prefs.DesiredCategoryIds).Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList(),
            MinSalary = prefs.MinSalary,
            PreferRemote = prefs.PreferRemote,
            Keywords = Split(prefs.Keywords)
        });
    }

    [HttpPost("preferences")]
    public async Task<ActionResult<PreferencesDto>> SavePreferences(PreferencesDto dto)
    {
        var prefs = await _context.JobPreferences.FirstOrDefaultAsync(p => p.UserId == UserId);

        if (prefs == null)
        {
            prefs = new JobPreferences { UserId = UserId };
            _context.JobPreferences.Add(prefs);
        }

        prefs.DesiredContractTypes = Join(dto.DesiredContractTypes);
        prefs.DesiredLocations = Join(dto.DesiredLocations);
        prefs.DesiredCategoryIds = string.Join(",", dto.DesiredCategoryIds);
        prefs.MinSalary = dto.MinSalary;
        prefs.PreferRemote = dto.PreferRemote;
        prefs.Keywords = Join(dto.Keywords);
        prefs.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(dto);
    }

    // GET recommandations
    [HttpGet("jobs")]
    public async Task<ActionResult<List<RecommendedJobDto>>> GetRecommendedJobs([FromQuery] int limit = 12)
    {
        var user = await _context.Users.FindAsync(UserId);
        if (user == null) return NotFound();

        var prefs = await _context.JobPreferences.FirstOrDefaultAsync(p => p.UserId == UserId);

        var jobs = await _context.JobOffers
            .Include(j => j.Company)
            .Include(j => j.Category)
            .Where(j => j.IsActive)
            .ToListAsync();

        // IDs des offres deja postulees
        var appliedList = await _context.Applications
            .Where(a => a.UserId == UserId)
            .Select(a => a.JobOfferId)
            .ToListAsync();
        var appliedIds = new HashSet<int>(appliedList);

        var userSkills = Split(user.Skills).Select(s => s.ToLower().Trim()).Where(s => s.Length > 0).ToList();
        var prefKeywords = prefs != null ? Split(prefs.Keywords).Select(s => s.ToLower().Trim()).ToList() : new List<string>();
        var prefLocations = prefs != null ? Split(prefs.DesiredLocations).Select(s => s.ToLower().Trim()).ToList() : new List<string>();
        var prefContracts = prefs != null ? Split(prefs.DesiredContractTypes) : new List<string>();
        var prefCategories = prefs != null ? Split(prefs.DesiredCategoryIds).Where(s => int.TryParse(s, out _)).Select(int.Parse).ToHashSet() : new HashSet<int>();
        var prefRemote = prefs?.PreferRemote;
        var prefMinSalary = prefs?.MinSalary;

        var allKeywords = userSkills.Concat(prefKeywords).Distinct().ToList();

        var scored = new List<RecommendedJobDto>();

        foreach (var job in jobs)
        {
            if (appliedIds.Contains(job.Id)) continue;

            var score = 0;
            var reasons = new List<string>();
            var titleDesc = (job.Title + " " + job.Description).ToLower();

            // 1. Skills/Keywords match (max 40 pts)
            var matchedSkills = allKeywords.Where(k => titleDesc.Contains(k)).ToList();
            if (matchedSkills.Count > 0)
            {
                var skillScore = Math.Min(40, matchedSkills.Count * 10);
                score += skillScore;
                reasons.Add($"Competences : {string.Join(", ", matchedSkills.Take(4))}");
            }

            // 2. Category match (20 pts)
            if (prefCategories.Count > 0 && prefCategories.Contains(job.CategoryId))
            {
                score += 20;
                reasons.Add($"Categorie : {job.Category.Name}");
            }

            // 3. Location match (15 pts)
            if (prefLocations.Count > 0 && prefLocations.Any(l => job.Location.ToLower().Contains(l)))
            {
                score += 15;
                reasons.Add($"Ville : {job.Location}");
            }

            // 4. Contract match (10 pts)
            if (prefContracts.Count > 0 && prefContracts.Contains(job.ContractType))
            {
                score += 10;
                reasons.Add($"Contrat : {job.ContractType}");
            }

            // 5. Remote match (10 pts)
            if (prefRemote == true && job.IsRemote)
            {
                score += 10;
                reasons.Add("Teletravail possible");
            }

            // 6. Salary match (5 pts)
            if (prefMinSalary.HasValue && job.SalaryMax.HasValue && job.SalaryMax >= prefMinSalary)
            {
                score += 5;
                reasons.Add("Salaire compatible");
            }

            // Bonus recence (max 10 pts)
            var daysSincePublished = (DateTime.UtcNow - job.PublishedAt).Days;
            if (daysSincePublished <= 7) { score += 10; reasons.Add("Publiee recemment"); }
            else if (daysSincePublished <= 14) score += 5;

            if (score > 0)
            {
                scored.Add(new RecommendedJobDto
                {
                    Job = MapJobToDto(job),
                    MatchScore = Math.Min(100, score),
                    MatchReasons = reasons
                });
            }
        }

        var result = scored.OrderByDescending(s => s.MatchScore).Take(limit).ToList();

        // Si pas assez de resultats, completer avec les plus recentes
        if (result.Count < limit)
        {
            var existingIds = result.Select(r => r.Job.Id).ToHashSet();
            var filler = jobs
                .Where(j => !appliedIds.Contains(j.Id) && !existingIds.Contains(j.Id))
                .OrderByDescending(j => j.PublishedAt)
                .Take(limit - result.Count)
                .Select(j => new RecommendedJobDto
                {
                    Job = MapJobToDto(j),
                    MatchScore = 0,
                    MatchReasons = new List<string> { "Offre recente" }
                });
            result.AddRange(filler);
        }

        return Ok(result);
    }

    [HttpGet("has-preferences")]
    public async Task<ActionResult> HasPreferences()
    {
        var has = await _context.JobPreferences.AnyAsync(p => p.UserId == UserId);
        return Ok(new { hasPreferences = has });
    }

    private static List<string> Split(string? s) =>
        string.IsNullOrWhiteSpace(s) ? new List<string>() : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Join(List<string> list) =>
        string.Join(",", list.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static JobOfferDto MapJobToDto(JobOffer j) => new()
    {
        Id = j.Id, Title = j.Title, Description = j.Description, Location = j.Location,
        ContractType = j.ContractType, SalaryMin = j.SalaryMin, SalaryMax = j.SalaryMax,
        IsRemote = j.IsRemote, PublishedAt = j.PublishedAt, ExpiresAt = j.ExpiresAt,
        IsActive = j.IsActive, CompanyId = j.CompanyId, CompanyName = j.Company.Name,
        CompanyLogoUrl = j.Company.LogoUrl, CategoryId = j.CategoryId, CategoryName = j.Category.Name
    };
}
