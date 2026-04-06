using System.Security.Claims;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LaPlateformeDeLemploiAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public ProfileController(AppDbContext context) => _context = context;

    [HttpPut]
    public async Task<ActionResult<UserDto>> Update(UpdateProfileDto dto)
    {
        var user = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == UserId);
        if (user == null) return NotFound();

        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;
        user.Phone = dto.Phone;
        user.Bio = dto.Bio;
        user.Skills = dto.Skills;

        await _context.SaveChangesAsync();

        return Ok(new UserDto
        {
            Id = user.Id, Email = user.Email,
            FirstName = user.FirstName, LastName = user.LastName,
            Role = user.Role, Phone = user.Phone,
            CompanyId = user.CompanyId, CompanyName = user.Company?.Name,
            Bio = user.Bio, Skills = user.Skills
        });
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardStatsDto>> GetDashboardStats()
    {
        var user = await _context.Users.FindAsync(UserId);
        if (user == null) return NotFound();

        var stats = new DashboardStatsDto();

        if (user.Role == "JobSeeker")
        {
            var apps = _context.Applications.Where(a => a.UserId == UserId);
            stats.TotalApplications = await apps.CountAsync();
            stats.PendingApplications = await apps.CountAsync(a => a.Status == "Pending");
            stats.AcceptedApplications = await apps.CountAsync(a => a.Status == "Accepted");
            stats.RejectedApplications = await apps.CountAsync(a => a.Status == "Rejected");
            stats.TotalFavorites = await _context.Favorites.CountAsync(f => f.UserId == UserId);
        }
        else if (user.Role == "Company" && user.CompanyId.HasValue)
        {
            var jobs = _context.JobOffers.Where(j => j.CompanyId == user.CompanyId);
            stats.TotalJobOffers = await jobs.CountAsync();
            stats.ActiveJobOffers = await jobs.CountAsync(j => j.IsActive);
            var jobIds = await jobs.Select(j => j.Id).ToListAsync();
            var received = _context.Applications.Where(a => jobIds.Contains(a.JobOfferId));
            stats.TotalReceivedApplications = await received.CountAsync();
            stats.PendingReceivedApplications = await received.CountAsync(a => a.Status == "Pending");
        }

        return Ok(stats);
    }

    [HttpGet("analytics")]
    public async Task<ActionResult<AdvancedStatsDto>> GetAnalytics()
    {
        var user = await _context.Users.FindAsync(UserId);
        if (user == null) return NotFound();

        var stats = new AdvancedStatsDto();
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        if (user.Role == "Company" && user.CompanyId.HasValue)
        {
            var jobIds = await _context.JobOffers
                .Where(j => j.CompanyId == user.CompanyId)
                .Select(j => j.Id).ToListAsync();

            var apps = _context.Applications
                .Include(a => a.JobOffer)
                .Where(a => jobIds.Contains(a.JobOfferId));

            // Par jour
            var daily = await apps.Where(a => a.AppliedAt >= thirtyDaysAgo)
                .GroupBy(a => a.AppliedAt.Date)
                .Select(g => new DailyCountDto { Date = g.Key.ToString("yyyy-MM-dd"), Count = g.Count() })
                .OrderBy(d => d.Date).ToListAsync();
            stats.ApplicationsPerDay = daily;

            // Par statut
            stats.ApplicationsByStatus = await apps
                .GroupBy(a => a.Status)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Par contrat
            stats.ApplicationsByContract = await apps
                .GroupBy(a => a.JobOffer.ContractType)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Top offres
            stats.TopOffers = await apps
                .GroupBy(a => a.JobOffer.Title)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => new TopOfferDto { Title = g.Key, ApplicationCount = g.Count() })
                .ToListAsync();

            // Par ville
            stats.ApplicationsByLocation = await apps
                .GroupBy(a => a.JobOffer.Location)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
        }
        else if (user.Role == "JobSeeker")
        {
            var apps = _context.Applications
                .Include(a => a.JobOffer)
                .Where(a => a.UserId == UserId);

            var daily = await apps.Where(a => a.AppliedAt >= thirtyDaysAgo)
                .GroupBy(a => a.AppliedAt.Date)
                .Select(g => new DailyCountDto { Date = g.Key.ToString("yyyy-MM-dd"), Count = g.Count() })
                .OrderBy(d => d.Date).ToListAsync();
            stats.ApplicationsPerDay = daily;

            stats.ApplicationsByStatus = await apps
                .GroupBy(a => a.Status)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            stats.ApplicationsByContract = await apps
                .GroupBy(a => a.JobOffer.ContractType)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            stats.ApplicationsByLocation = await apps
                .GroupBy(a => a.JobOffer.Location)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
        }

        return Ok(stats);
    }
}
