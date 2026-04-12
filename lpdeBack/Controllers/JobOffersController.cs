using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.DTOs;
using lpdeBack.Hubs;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobOffersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<AppUser> _userManager;

    public JobOffersController(AppDbContext context, UserManager<AppUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin() => User.IsInRole("Admin");

    // ── Public endpoints (no auth) ──

    [HttpGet]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? contractType,
        [FromQuery] bool? isRemote,
        [FromQuery] string? location,
        [FromQuery] int? salaryMin,
        [FromQuery] int? salaryMax,
        [FromQuery] string? experience,
        [FromQuery] string? education,
        [FromQuery] string? sort)
    {
        // Auto-expire offers past their expiration date
        await _context.JobOffers
            .Where(j => j.IsActive && j.ExpiresAt != null && j.ExpiresAt < DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.IsActive, false));

        var query = _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved").AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(j => j.Title.Contains(search) || j.Company.Contains(search) || j.Description.Contains(search));

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(j => j.Category == category);

        if (!string.IsNullOrWhiteSpace(contractType))
            query = query.Where(j => j.ContractType == contractType);

        if (isRemote.HasValue)
            query = query.Where(j => j.IsRemote == isRemote.Value);

        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(j => j.Location.Contains(location));

        if (salaryMin.HasValue)
            query = query.Where(j => j.MaxSalary >= salaryMin.Value || (j.MinSalary.HasValue && j.MinSalary >= salaryMin.Value));

        if (salaryMax.HasValue)
            query = query.Where(j => j.MinSalary <= salaryMax.Value || (j.MaxSalary.HasValue && j.MaxSalary <= salaryMax.Value));

        if (!string.IsNullOrWhiteSpace(experience))
            query = query.Where(j => j.ExperienceRequired == experience);

        if (!string.IsNullOrWhiteSpace(education))
            query = query.Where(j => j.EducationLevel == education);

        // Sorting
        query = sort switch
        {
            "salary_asc" => query.OrderBy(j => j.MinSalary ?? 0),
            "salary_desc" => query.OrderByDescending(j => j.MaxSalary ?? 0),
            "views" => query.OrderByDescending(j => j.ViewCount),
            _ => query.OrderByDescending(j => j.CreatedAt),
        };

        return await query.ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<JobOffer>> GetById(int id)
    {
        var userId = GetUserId();
        var job = await _context.JobOffers.Include(j => j.Applications).FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();

        // Increment view count
        job.ViewCount++;
        await _context.SaveChangesAsync();

        // Only show applications if the logged-in user is the creator of this offer or an admin
        var isOwner = userId != null && job.CreatedByUserId == userId;
        if (!isOwner && !IsAdmin())
        {
            job.Applications = new List<Application>();
        }

        return job;
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<string>>> GetCategories()
    {
        return await _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved").Select(j => j.Category).Distinct().OrderBy(c => c).ToListAsync();
    }

    [HttpGet("stats")]
    public async Task<ActionResult<object>> GetStats()
    {
        var totalOffers = await _context.JobOffers.CountAsync(j => j.IsActive && j.ModerationStatus == "Approved");
        var totalApplications = await _context.Applications.CountAsync();
        var totalCompanies = await _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved").Select(j => j.Company).Distinct().CountAsync();
        var remoteOffers = await _context.JobOffers.CountAsync(j => j.IsActive && j.ModerationStatus == "Approved" && j.IsRemote);

        return new { totalOffers, totalApplications, totalCompanies, remoteOffers };
    }

    [HttpGet("moderation-required")]
    public async Task<ActionResult<object>> IsModerationRequired()
    {
        var val = await _context.PlatformSettings
            .Where(s => s.Key == "require_moderation")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        return new { required = val == "true" };
    }

    [HttpGet("companies")]
    public async Task<ActionResult<IEnumerable<object>>> GetCompanies()
    {
        var companies = await _context.JobOffers
            .Where(j => j.IsActive && j.ModerationStatus == "Approved")
            .GroupBy(j => j.Company)
            .Select(g => new { company = g.Key, jobCount = g.Count(), locations = g.Select(j => j.Location).Distinct().ToList() })
            .OrderByDescending(c => c.jobCount)
            .ToListAsync();
        return Ok(companies);
    }

    [HttpGet("company/{companyName}")]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetByCompany(string companyName)
    {
        return await _context.JobOffers
            .Where(j => j.IsActive && j.ModerationStatus == "Approved" && j.Company == companyName)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    // ── Protected endpoints (Recruiter / Admin) ──

    [HttpGet("mine")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetMyOffers()
    {
        var userId = GetUserId();
        var query = IsAdmin()
            ? _context.JobOffers.AsQueryable()
            : _context.JobOffers.Where(j => j.CreatedByUserId == userId);

        return await query
            .Include(j => j.Applications)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    [HttpPatch("{id}/renew")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<JobOffer>> RenewOffer(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();
        if (!IsAdmin() && job.CreatedByUserId != GetUserId()) return Forbid();

        var durationStr = await _context.PlatformSettings
            .Where(s => s.Key == "default_offer_duration")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        var duration = int.TryParse(durationStr, out var d) ? d : 30;

        job.IsActive = true;
        job.ExpiresAt = DateTime.UtcNow.AddDays(duration);
        job.ModerationStatus = "Approved"; // Renewal re-approves
        await _context.SaveChangesAsync();
        return Ok(job);
    }

    [HttpGet("stats/detailed")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<object>> GetDetailedStats()
    {
        var offersByCategory = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Category)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToListAsync();

        var offersByContract = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.ContractType)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToListAsync();

        var appsByStatus = await _context.Applications
            .GroupBy(a => a.Status)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToListAsync();

        var topCompanies = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Company)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .Take(5)
            .ToListAsync();

        var recentApps = await _context.Applications
            .OrderByDescending(a => a.AppliedAt)
            .Take(30)
            .GroupBy(a => a.AppliedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .ToListAsync();

        return new { offersByCategory, offersByContract, appsByStatus, topCompanies, recentApps };
    }

    /// <summary>Admin: statistiques complètes de la plateforme</summary>
    [HttpGet("stats/admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> GetAdminStats()
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var sevenDaysAgo = now.AddDays(-7);

        // ── Utilisateurs ──
        var allUsers = await _userManager.Users.ToListAsync();
        var totalUsers = allUsers.Count;
        var totalCandidates = allUsers.Count(u => u.Role == "Candidate");
        var totalRecruiters = allUsers.Count(u => u.Role == "Recruiter");
        var totalAdmins = allUsers.Count(u => u.Role == "Admin");
        var usersLast30d = allUsers.Count(u => u.CreatedAt >= thirtyDaysAgo);
        var usersLast7d = allUsers.Count(u => u.CreatedAt >= sevenDaysAgo);
        var onlineNow = ChatHub.GetOnlineUserIds().Count();

        // Inscriptions par jour (30 derniers jours)
        var registrationsByDay = allUsers
            .Where(u => u.CreatedAt >= thirtyDaysAgo)
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // Inscriptions par rôle par semaine (8 dernières semaines)
        var eightWeeksAgo = now.AddDays(-56);
        var registrationsByRoleWeek = allUsers
            .Where(u => u.CreatedAt >= eightWeeksAgo)
            .GroupBy(u => new { Week = System.Globalization.ISOWeek.GetWeekOfYear(u.CreatedAt), u.Role })
            .Select(g => new { week = g.Key.Week, role = g.Key.Role, count = g.Count() })
            .OrderBy(x => x.week)
            .ToList();

        // Répartition des villes des utilisateurs
        var usersByCity = allUsers
            .Where(u => !string.IsNullOrEmpty(u.City))
            .GroupBy(u => u.City!.Trim())
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .Take(20)
            .ToList();

        // Candidats par ville (pour la carte)
        var candidatesByCity = allUsers
            .Where(u => u.Role == "Candidate" && !string.IsNullOrEmpty(u.City))
            .GroupBy(u => u.City!.Trim())
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Recruteurs par ville (pour la carte)
        var recruitersByCity = allUsers
            .Where(u => (u.Role == "Recruiter" || u.Role == "Admin") && !string.IsNullOrEmpty(u.City))
            .GroupBy(u => u.City!.Trim())
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // ── Offres ──
        var allOffers = await _context.JobOffers.ToListAsync();
        var totalOffers = allOffers.Count;
        var activeOffers = allOffers.Count(j => j.IsActive);
        var expiredOffers = allOffers.Count(j => !j.IsActive);
        var urgentOffers = allOffers.Count(j => j.IsUrgent && j.IsActive);
        var remoteOffers = allOffers.Count(j => j.IsRemote && j.IsActive);
        var offersLast30d = allOffers.Count(j => j.CreatedAt >= thirtyDaysAgo);
        var totalViews = allOffers.Sum(j => j.ViewCount);

        // Offres publiées par jour (30 derniers jours)
        var offersByDay = allOffers
            .Where(j => j.CreatedAt >= thirtyDaysAgo)
            .GroupBy(j => j.CreatedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // Offres par catégorie
        var offersByCategory = allOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Category)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Offres par type de contrat
        var offersByContract = allOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.ContractType)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Offres par niveau d'expérience
        var offersByExperience = allOffers
            .Where(j => j.IsActive && !string.IsNullOrEmpty(j.ExperienceRequired))
            .GroupBy(j => j.ExperienceRequired!)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Top offres les plus vues
        var topViewedOffers = allOffers
            .Where(j => j.IsActive)
            .OrderByDescending(j => j.ViewCount)
            .Take(10)
            .Select(j => new { label = j.Title.Length > 35 ? j.Title.Substring(0, 35) + "..." : j.Title, value = j.ViewCount, company = j.Company })
            .ToList();

        // Géographie des offres (par ville)
        var offersByLocation = allOffers
            .Where(j => j.IsActive && !string.IsNullOrEmpty(j.Location))
            .GroupBy(j => j.Location.Trim())
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .Take(20)
            .ToList();

        // Salaires moyens par catégorie
        var salaryByCategory = allOffers
            .Where(j => j.IsActive && j.MinSalary.HasValue && j.MaxSalary.HasValue)
            .GroupBy(j => j.Category)
            .Select(g => new { label = g.Key, min = (int)g.Average(j => j.MinSalary!.Value), max = (int)g.Average(j => j.MaxSalary!.Value) })
            .OrderByDescending(x => x.max)
            .ToList();

        // Top entreprises
        var topCompanies = allOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Company)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .Take(10)
            .ToList();

        // ── Candidatures ──
        var allApps = await _context.Applications.Include(a => a.JobOffer).ToListAsync();
        var totalApplications = allApps.Count;
        var appsLast30d = allApps.Count(a => a.AppliedAt >= thirtyDaysAgo);
        var appsLast7d = allApps.Count(a => a.AppliedAt >= sevenDaysAgo);

        // Candidatures par statut
        var appsByStatus = allApps
            .GroupBy(a => a.Status)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToList();

        // Candidatures par jour (30 derniers jours)
        var appsByDay = allApps
            .Where(a => a.AppliedAt >= thirtyDaysAgo)
            .GroupBy(a => a.AppliedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // Candidatures par source
        var appsBySource = allApps
            .Where(a => !string.IsNullOrEmpty(a.Source))
            .GroupBy(a => a.Source!)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Taux de conversion par entreprise (candidatures / vues)
        var conversionByCompany = allOffers
            .Where(j => j.IsActive && j.ViewCount > 0)
            .GroupBy(j => j.Company)
            .Select(g => new {
                label = g.Key,
                views = g.Sum(j => j.ViewCount),
                apps = allApps.Count(a => g.Select(j => j.Id).Contains(a.JobOfferId)),
            })
            .Where(x => x.apps > 0)
            .Select(x => new { x.label, value = Math.Round((double)x.apps / x.views * 100, 1) })
            .OrderByDescending(x => x.value)
            .Take(10)
            .ToList();

        // ── Entretiens ──
        var totalInterviews = await _context.Interviews.CountAsync();
        var interviewsByType = await _context.Interviews
            .Where(i => !string.IsNullOrEmpty(i.Type))
            .GroupBy(i => i.Type!)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToListAsync();

        var interviewsByStatus = await _context.Interviews
            .GroupBy(i => i.Status)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToListAsync();

        // ── Messagerie ──
        var totalMessages = await _context.Messages.CountAsync();
        var messagesLast30d = await _context.Messages.CountAsync(m => m.CreatedAt >= thirtyDaysAgo);
        var recentMessages = await _context.Messages
            .Where(m => m.CreatedAt >= thirtyDaysAgo)
            .Select(m => m.CreatedAt)
            .ToListAsync();
        var messagesByDay = recentMessages
            .GroupBy(d => d.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // ── Activité globale (timeline combinée) ──
        // Comptage par jour : offres + candidatures + inscriptions
        var activityDays = Enumerable.Range(0, 30).Select(i => thirtyDaysAgo.AddDays(i).Date).ToList();
        var activityTimeline = activityDays.Select(day => new {
            label = day.ToString("dd/MM"),
            offres = allOffers.Count(j => j.CreatedAt.Date == day),
            candidatures = allApps.Count(a => a.AppliedAt.Date == day),
            inscriptions = allUsers.Count(u => u.CreatedAt.Date == day),
        }).ToList();

        return Ok(new {
            // KPI principaux
            totalUsers, totalCandidates, totalRecruiters, totalAdmins,
            usersLast30d, usersLast7d, onlineNow,
            totalOffers, activeOffers, expiredOffers, urgentOffers, remoteOffers, offersLast30d, totalViews,
            totalApplications, appsLast30d, appsLast7d,
            totalInterviews, totalMessages, messagesLast30d,

            // Charts
            registrationsByDay, registrationsByRoleWeek,
            usersByCity, candidatesByCity, recruitersByCity,
            offersByDay, offersByCategory, offersByContract, offersByExperience,
            topViewedOffers, offersByLocation, salaryByCategory, topCompanies,
            appsByStatus, appsByDay, appsBySource, conversionByCompany,
            interviewsByType, interviewsByStatus,
            messagesByDay, activityTimeline,
        });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<JobOffer>> Create(JobOfferCreateDto dto)
    {
        // Check if moderation is required
        var requireModeration = await _context.PlatformSettings
            .Where(s => s.Key == "require_moderation")
            .Select(s => s.Value)
            .FirstOrDefaultAsync() == "true";

        // Admins bypass moderation
        var isAdmin = IsAdmin();
        var needsReview = requireModeration && !isAdmin;

        // Get default offer duration from settings
        var durationStr = await _context.PlatformSettings
            .Where(s => s.Key == "default_offer_duration")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        var duration = int.TryParse(durationStr, out var d) ? d : 30;

        var job = new JobOffer
        {
            Title = dto.Title,
            Company = dto.Company,
            Location = dto.Location,
            Description = dto.Description,
            ContractType = dto.ContractType,
            Salary = dto.Salary,
            Category = dto.Category,
            IsRemote = dto.IsRemote,
            ExpiresAt = dto.ExpiresAt ?? DateTime.UtcNow.AddDays(duration),
            CompanyLogoUrl = dto.CompanyLogoUrl,
            Tags = dto.Tags,
            MinSalary = dto.MinSalary,
            MaxSalary = dto.MaxSalary,
            ExperienceRequired = dto.ExperienceRequired,
            EducationLevel = dto.EducationLevel,
            Benefits = dto.Benefits,
            CompanyDescription = dto.CompanyDescription,
            IsUrgent = dto.IsUrgent,
            CreatedByUserId = GetUserId(),
            ModerationStatus = needsReview ? "Pending" : "Approved",
            IsActive = !needsReview,
        };

        _context.JobOffers.Add(job);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Update(int id, JobOfferUpdateDto dto)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        job.Title = dto.Title;
        job.Company = dto.Company;
        job.Location = dto.Location;
        job.Description = dto.Description;
        job.ContractType = dto.ContractType;
        job.Salary = dto.Salary;
        job.Category = dto.Category;
        job.IsRemote = dto.IsRemote;
        job.ExpiresAt = dto.ExpiresAt;
        job.CompanyLogoUrl = dto.CompanyLogoUrl;
        job.Tags = dto.Tags;
        job.MinSalary = dto.MinSalary;
        job.MaxSalary = dto.MaxSalary;
        job.ExperienceRequired = dto.ExperienceRequired;
        job.EducationLevel = dto.EducationLevel;
        job.Benefits = dto.Benefits;
        job.CompanyDescription = dto.CompanyDescription;
        job.IsUrgent = dto.IsUrgent;
        job.IsActive = dto.IsActive;

        // Re-submit to moderation if moderation is enabled (admin bypass)
        if (!IsAdmin())
        {
            var requireModeration = await _context.PlatformSettings
                .Where(s => s.Key == "require_moderation")
                .Select(s => s.Value)
                .FirstOrDefaultAsync() == "true";

            if (requireModeration)
            {
                job.ModerationStatus = "Pending";
                job.ModerationNote = null;
                job.IsActive = false;
            }
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        _context.JobOffers.Remove(job);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
