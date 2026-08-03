using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;
using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/recruiter")]
[Authorize(Roles = "Admin,Recruiter")]
public class RecruiterFeaturesController : ControllerBase
{
    private readonly AppDbContext _context;
    public RecruiterFeaturesController(AppDbContext context) => _context = context;
    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool IsAdmin() => User.IsInRole("Admin");

    // ═══════════════════════════════════
    //  1. DUPLICATION D'OFFRES
    // ═══════════════════════════════════

    [HttpPost("offers/{id}/duplicate")]
    public async Task<ActionResult<JobOffer>> DuplicateOffer(int id)
    {
        var src = await _context.JobOffers.FindAsync(id);
        if (src == null) return NotFound();
        if (!IsAdmin() && src.CreatedByUserId != UserId()) return Forbid();

        var durationStr = await _context.PlatformSettings
            .Where(s => s.Key == "default_offer_duration").Select(s => s.Value).FirstOrDefaultAsync();
        var duration = int.TryParse(durationStr, out var d) ? d : 30;

        // La copie part en brouillon : elle porte « (copie) » dans son titre et
        // attend d'etre relue dans le tunnel de publication avant d'etre
        // proposee aux candidats.
        var dup = new JobOffer
        {
            Title = src.Title + " (copie)",
            Company = src.Company,
            Location = src.Location,
            Address = src.Address,
            WorkplaceType = src.WorkplaceType,
            Description = src.Description,
            ContractType = src.ContractType,
            ContractDuration = src.ContractDuration,
            WorkSchedule = src.WorkSchedule,
            HoursPerWeek = src.HoursPerWeek,
            StartDate = src.StartDate,
            Openings = src.Openings,
            Salary = src.Salary,
            SalaryPeriod = src.SalaryPeriod,
            SupplementalPay = src.SupplementalPay,
            Category = src.Category,
            IsRemote = src.IsRemote,
            Tags = src.Tags,
            MinSalary = src.MinSalary,
            MaxSalary = src.MaxSalary,
            ExperienceRequired = src.ExperienceRequired,
            EducationLevel = src.EducationLevel,
            Languages = src.Languages,
            Benefits = src.Benefits,
            CompanyDescription = src.CompanyDescription,
            CompanyLogoUrl = src.CompanyLogoUrl,
            IsUrgent = src.IsUrgent,
            EasyApply = src.EasyApply,
            RequireResume = src.RequireResume,
            ApplicationEmail = src.ApplicationEmail,
            ScreeningQuestions = src.ScreeningQuestions,
            AutoReplyMessage = src.AutoReplyMessage,
            Latitude = src.Latitude,
            Longitude = src.Longitude,
            CreatedByUserId = UserId(),
            ExpiresAt = DateTime.UtcNow.AddDays(duration),
            ModerationStatus = "Approved",
            IsDraft = true,
            IsActive = false,
        };

        _context.JobOffers.Add(dup);
        await _context.SaveChangesAsync();
        return Ok(dup);
    }

    // ═══════════════════════════════════
    //  2. RECHERCHE AVANCEE DE CANDIDATS
    // ═══════════════════════════════════

    [HttpGet("candidates/search")]
    public async Task<ActionResult<IEnumerable<object>>> SearchCandidates(
        [FromQuery] string? search,
        [FromQuery] string? skills,
        [FromQuery] string? city,
        [FromQuery] int? minExperience,
        [FromQuery] int? maxExperience,
        [FromQuery] string? education,
        [FromQuery] string? sort)
    {
        var query = _context.Users.Where(u => u.Role == "Candidate" && u.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.FirstName.Contains(search) || u.LastName.Contains(search) || (u.Bio != null && u.Bio.Contains(search)) || (u.Title != null && u.Title.Contains(search)));

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(u => u.City != null && u.City.Contains(city));

        if (minExperience.HasValue)
            query = query.Where(u => u.ExperienceYears.HasValue && u.ExperienceYears >= minExperience.Value);

        if (maxExperience.HasValue)
            query = query.Where(u => u.ExperienceYears.HasValue && u.ExperienceYears <= maxExperience.Value);

        if (!string.IsNullOrWhiteSpace(education))
            query = query.Where(u => u.Education != null && u.Education.Contains(education));

        if (!string.IsNullOrWhiteSpace(skills))
        {
            var skillList = skills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var skill in skillList)
                query = query.Where(u => u.Skills != null && u.Skills.Contains(skill));
        }

        query = sort switch
        {
            "experience_desc" => query.OrderByDescending(u => u.ExperienceYears ?? 0),
            "experience_asc" => query.OrderBy(u => u.ExperienceYears ?? 0),
            "name" => query.OrderBy(u => u.LastName),
            _ => query.OrderByDescending(u => u.CreatedAt),
        };

        var candidates = await query.Take(100).ToListAsync();
        var result = new List<object>();
        foreach (var c in candidates)
        {
            var appCount = await _context.Applications.CountAsync(a => a.UserId == c.Id);
            result.Add(new
            {
                c.Id, c.FirstName, c.LastName, c.AvatarUrl, c.Title, c.Skills,
                c.ExperienceYears, c.Education, c.City, c.Bio, c.CreatedAt,
                applicationCount = appCount,
            });
        }
        return Ok(result);
    }

    // ═══════════════════════════════════
    //  3. TEMPLATES DE MESSAGES
    // ═══════════════════════════════════

    [HttpGet("templates")]
    public async Task<ActionResult<IEnumerable<MessageTemplate>>> GetTemplates()
    {
        var uid = UserId();
        // Include system defaults (userId null) + user's own
        return await _context.MessageTemplates
            .Where(t => t.UserId == uid)
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .ToListAsync();
    }

    [HttpPost("templates")]
    public async Task<ActionResult<MessageTemplate>> CreateTemplate(TemplateDto dto)
    {
        var template = new MessageTemplate
        {
            UserId = UserId(),
            Name = dto.Name,
            Content = dto.Content,
            Category = dto.Category ?? "General",
        };
        _context.MessageTemplates.Add(template);
        await _context.SaveChangesAsync();
        return Ok(template);
    }

    [HttpPut("templates/{id}")]
    public async Task<IActionResult> UpdateTemplate(int id, TemplateDto dto)
    {
        var t = await _context.MessageTemplates.FindAsync(id);
        if (t == null || t.UserId != UserId()) return NotFound();
        t.Name = dto.Name;
        t.Content = dto.Content;
        t.Category = dto.Category ?? t.Category;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("templates/{id}")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        var t = await _context.MessageTemplates.FindAsync(id);
        if (t == null || t.UserId != UserId()) return NotFound();
        _context.MessageTemplates.Remove(t);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══════════════════════════════════
    //  4. STATS PAR OFFRE
    // ═══════════════════════════════════

    [HttpGet("offers/{id}/stats")]
    public async Task<ActionResult<object>> GetOfferStats(int id)
    {
        var job = await _context.JobOffers.Include(j => j.Applications).FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();
        if (!IsAdmin() && job.CreatedByUserId != UserId()) return Forbid();

        var apps = job.Applications.ToList();
        var interviews = await _context.Interviews
            .Where(i => apps.Select(a => a.Id).Contains(i.ApplicationId))
            .ToListAsync();

        return new
        {
            job.Id, job.Title, job.ViewCount,
            totalApplications = apps.Count,
            pending = apps.Count(a => a.Status == "Pending"),
            reviewed = apps.Count(a => a.Status == "Reviewed"),
            accepted = apps.Count(a => a.Status == "Accepted"),
            rejected = apps.Count(a => a.Status == "Rejected"),
            totalInterviews = interviews.Count,
            conversionRate = job.ViewCount > 0 ? Math.Round((double)apps.Count / job.ViewCount * 100, 1) : 0,
            appsByDay = apps
                .Where(a => a.AppliedAt >= DateTime.UtcNow.AddDays(-30))
                .GroupBy(a => a.AppliedAt.Date)
                .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
                .OrderBy(x => x.label).ToList(),
            funnel = new[] {
                new { label = "Vues", value = job.ViewCount },
                new { label = "Candidatures", value = apps.Count },
                new { label = "Entretiens", value = interviews.Count },
                new { label = "Acceptees", value = apps.Count(a => a.Status == "Accepted") },
            },
        };
    }

    // ═══════════════════════════════════
    //  5. ACTIONS GROUPEES
    // ═══════════════════════════════════

    [HttpPatch("applications/bulk-status")]
    public async Task<IActionResult> BulkUpdateStatus(BulkStatusDto dto)
    {
        if (!StatutCandidature.Existe(dto.Status)) return BadRequest("Statut invalide.");

        var uid = UserId();
        var apps = await _context.Applications
            .Include(a => a.JobOffer)
            .Where(a => dto.Ids.Contains(a.Id))
            .ToListAsync();

        foreach (var app in apps)
        {
            if (!IsAdmin() && app.JobOffer.CreatedByUserId != uid) continue;
            app.Status = dto.Status;
        }

        await _context.SaveChangesAsync();
        return Ok(new { updated = apps.Count });
    }
}

// DTOs
public class TemplateDto
{
    [Required(ErrorMessage = "Donnez un nom à ce modèle.")]
    [StringLength(Limites.Ligne, MinimumLength = 2)]
    [SansBalisage]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Écrivez le contenu du modèle.")]
    [StringLength(Limites.Texte, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;

    [Longueur(Limites.Nom), SansBalisage]
    public string? Category { get; set; }
}

public class BulkStatusDto
{
    // Le lot est borne : chaque element declenche un courriel au candidat.
    // Sans borne, un seul appel peut en expedier des milliers, et rien ne
    // les rattrape une fois partis.
    [MinLength(1, ErrorMessage = "Sélectionnez au moins une candidature.")]
    [MaxLength(500, ErrorMessage = "Traitez au maximum 500 candidatures à la fois.")]
    public List<int> Ids { get; set; } = new();

    [Required(ErrorMessage = "Indiquez le statut.")]
    [StatutCandidature]
    public string Status { get; set; } = string.Empty;
}
