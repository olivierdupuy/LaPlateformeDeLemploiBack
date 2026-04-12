using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Hubs;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<AppUser> _userManager;
    private readonly ActivityLogService _log;
    private readonly IHubContext<ChatHub> _hub;

    public AdminController(AppDbContext context, UserManager<AppUser> userManager, ActivityLogService log, IHubContext<ChatHub> hub)
    {
        _context = context;
        _userManager = userManager;
        _log = log;
        _hub = hub;
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string UserFullName() => $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}";
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    // ═══════════════════════════════════
    //  1. JOURNAL D'ACTIVITE
    // ═══════════════════════════════════

    [HttpGet("activity-logs")]
    public async Task<ActionResult<object>> GetActivityLogs(
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _context.ActivityLogs.AsQueryable();

        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(userId)) query = query.Where(a => a.UserId == userId);

        var total = await query.CountAsync();
        var logs = await query.OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return new { logs, total, page, pageSize };
    }

    [HttpGet("activity-logs/actions")]
    public async Task<ActionResult<IEnumerable<string>>> GetLogActions()
    {
        return await _context.ActivityLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
    }

    // ═══════════════════════════════════
    //  2. MODERATION DES OFFRES
    // ═══════════════════════════════════

    [HttpGet("moderation")]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetModerationQueue([FromQuery] string? status)
    {
        var query = _context.JobOffers.Include(j => j.CreatedByUser).AsQueryable();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(j => j.ModerationStatus == status);
        else
            query = query.Where(j => j.ModerationStatus == "Pending");

        return await query.OrderByDescending(j => j.CreatedAt).ToListAsync();
    }

    [HttpPatch("moderation/{id}/approve")]
    public async Task<IActionResult> ApproveOffer(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        job.ModerationStatus = "Approved";
        job.IsActive = true;
        await _context.SaveChangesAsync();

        await _log.Log("ApproveOffer", "JobOffer", id, $"Offre approuvee: {job.Title}", UserId(), UserFullName(), Ip());
        return Ok(new { job.Id, job.ModerationStatus });
    }

    [HttpPatch("moderation/{id}/reject")]
    public async Task<IActionResult> RejectOffer(int id, [FromBody] ModerationNoteDto dto)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        job.ModerationStatus = "Rejected";
        job.IsActive = false;
        job.ModerationNote = dto.Note;
        await _context.SaveChangesAsync();

        await _log.Log("RejectOffer", "JobOffer", id, $"Offre rejetee: {job.Title} — {dto.Note}", UserId(), UserFullName(), Ip());
        return Ok(new { job.Id, job.ModerationStatus });
    }

    [HttpPatch("moderation/{id}/feature")]
    public async Task<IActionResult> ToggleFeature(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        job.IsFeatured = !job.IsFeatured;
        await _context.SaveChangesAsync();

        await _log.Log("ToggleFeature", "JobOffer", id, $"Offre {(job.IsFeatured ? "mise en avant" : "retiree de la une")}: {job.Title}", UserId(), UserFullName(), Ip());
        return Ok(new { job.Id, job.IsFeatured });
    }

    // ═══════════════════════════════════
    //  3. ANNONCES & COMMUNICATION
    // ═══════════════════════════════════

    [HttpGet("announcements")]
    public async Task<ActionResult<IEnumerable<Announcement>>> GetAnnouncements()
    {
        return await _context.Announcements.OrderByDescending(a => a.CreatedAt).ToListAsync();
    }

    [HttpPost("announcements")]
    public async Task<ActionResult<Announcement>> CreateAnnouncement(AnnouncementCreateDto dto)
    {
        var ann = new Announcement
        {
            Title = dto.Title,
            Message = dto.Message,
            Type = dto.Type ?? "info",
            TargetRole = dto.TargetRole,
            IsBanner = dto.IsBanner,
            StartsAt = dto.StartsAt,
            EndsAt = dto.EndsAt,
            CreatedByUserId = UserId(),
        };

        _context.Announcements.Add(ann);
        await _context.SaveChangesAsync();

        // If it's a notification (not just a banner), send to users
        if (!dto.IsBanner)
        {
            var users = _userManager.Users.AsQueryable();
            if (!string.IsNullOrEmpty(dto.TargetRole))
                users = users.Where(u => u.Role == dto.TargetRole);

            var userList = await users.Select(u => u.Id).ToListAsync();
            foreach (var uid in userList)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = uid,
                    Title = dto.Title,
                    Message = dto.Message,
                    Type = "Annonce",
                    Link = "/",
                });
            }
            await _context.SaveChangesAsync();

            // Push via SignalR
            foreach (var uid in userList)
            {
                foreach (var connId in ChatHub.GetConnectionIds(uid))
                    await _hub.Clients.Client(connId).SendAsync("NewNotification");
            }
        }

        await _log.Log("CreateAnnouncement", "Announcement", ann.Id, $"Annonce creee: {ann.Title}", UserId(), UserFullName(), Ip());
        return CreatedAtAction(nameof(GetAnnouncements), new { id = ann.Id }, ann);
    }

    [HttpDelete("announcements/{id}")]
    public async Task<IActionResult> DeleteAnnouncement(int id)
    {
        var ann = await _context.Announcements.FindAsync(id);
        if (ann == null) return NotFound();
        _context.Announcements.Remove(ann);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("announcements/{id}/toggle")]
    public async Task<IActionResult> ToggleAnnouncement(int id)
    {
        var ann = await _context.Announcements.FindAsync(id);
        if (ann == null) return NotFound();
        ann.IsActive = !ann.IsActive;
        await _context.SaveChangesAsync();
        return Ok(new { ann.Id, ann.IsActive });
    }

    // Public endpoint — active banners (no auth required)
    [HttpGet("banners")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<object>>> GetActiveBanners()
    {
        var now = DateTime.UtcNow;
        var banners = await _context.Announcements
            .Where(a => a.IsActive && a.IsBanner &&
                        (a.StartsAt == null || a.StartsAt <= now) &&
                        (a.EndsAt == null || a.EndsAt >= now))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.Id, a.Title, a.Message, a.Type })
            .ToListAsync();
        return Ok(banners);
    }

    // ═══════════════════════════════════
    //  4. EXPORT CSV
    // ═══════════════════════════════════

    [HttpGet("export/users")]
    public async Task<IActionResult> ExportUsers()
    {
        var users = await _userManager.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
        var csv = new StringBuilder();
        csv.AppendLine("Id,Prenom,Nom,Email,Role,Entreprise,Ville,Inscription,Actif,En ligne");
        foreach (var u in users)
            csv.AppendLine($"\"{u.Id}\",\"{u.FirstName}\",\"{u.LastName}\",\"{u.Email}\",\"{u.Role}\",\"{u.Company}\",\"{u.City}\",\"{u.CreatedAt:yyyy-MM-dd}\",\"{u.IsActive}\",\"{ChatHub.IsUserOnline(u.Id)}\"");

        await _log.Log("ExportCSV", "User", null, $"Export {users.Count} utilisateurs", UserId(), UserFullName(), Ip());
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"utilisateurs_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("export/offers")]
    public async Task<IActionResult> ExportOffers()
    {
        var offers = await _context.JobOffers.Include(j => j.Applications).OrderByDescending(j => j.CreatedAt).ToListAsync();
        var csv = new StringBuilder();
        csv.AppendLine("Id,Titre,Entreprise,Ville,Categorie,Contrat,Remote,SalaireMin,SalaireMax,Vues,Candidatures,Active,Creation,Expiration,Moderation");
        foreach (var j in offers)
            csv.AppendLine($"{j.Id},\"{j.Title}\",\"{j.Company}\",\"{j.Location}\",\"{j.Category}\",\"{j.ContractType}\",{j.IsRemote},{j.MinSalary},{j.MaxSalary},{j.ViewCount},{j.Applications.Count},{j.IsActive},\"{j.CreatedAt:yyyy-MM-dd}\",\"{j.ExpiresAt:yyyy-MM-dd}\",\"{j.ModerationStatus}\"");

        await _log.Log("ExportCSV", "JobOffer", null, $"Export {offers.Count} offres", UserId(), UserFullName(), Ip());
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"offres_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("export/applications")]
    public async Task<IActionResult> ExportApplications()
    {
        var apps = await _context.Applications.Include(a => a.JobOffer).OrderByDescending(a => a.AppliedAt).ToListAsync();
        var csv = new StringBuilder();
        csv.AppendLine("Id,Candidat,Email,Offre,Entreprise,Statut,Source,DateCandidature");
        foreach (var a in apps)
            csv.AppendLine($"{a.Id},\"{a.FullName}\",\"{a.Email}\",\"{a.JobOffer?.Title}\",\"{a.JobOffer?.Company}\",\"{a.Status}\",\"{a.Source}\",\"{a.AppliedAt:yyyy-MM-dd}\"");

        await _log.Log("ExportCSV", "Application", null, $"Export {apps.Count} candidatures", UserId(), UserFullName(), Ip());
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"candidatures_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ═══════════════════════════════════
    //  5. PARAMETRES PLATEFORME
    // ═══════════════════════════════════

    [HttpGet("settings")]
    public async Task<ActionResult<IEnumerable<PlatformSetting>>> GetSettings()
    {
        return await _context.PlatformSettings.OrderBy(s => s.Key).ToListAsync();
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(List<SettingUpdateDto> settings)
    {
        foreach (var dto in settings)
        {
            var setting = await _context.PlatformSettings.FirstOrDefaultAsync(s => s.Key == dto.Key);
            if (setting != null)
            {
                setting.Value = dto.Value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.PlatformSettings.Add(new PlatformSetting
                {
                    Key = dto.Key,
                    Value = dto.Value,
                    Type = dto.Type ?? "string",
                    Description = dto.Description ?? "",
                });
            }
        }
        await _context.SaveChangesAsync();

        await _log.Log("UpdateSettings", "PlatformSetting", null, $"Parametres mis a jour ({settings.Count} modif.)", UserId(), UserFullName(), Ip());
        return NoContent();
    }

    [HttpGet("settings/{key}")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> GetSetting(string key)
    {
        var setting = await _context.PlatformSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null) return NotFound();
        return new { setting.Key, setting.Value, setting.Type };
    }

    /// <summary>Public settings for frontend (no auth needed)</summary>
    [HttpGet("public-settings")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> GetPublicSettings()
    {
        var publicKeys = new[] { "welcome_message", "contact_email", "allow_registration", "require_moderation", "maintenance_mode", "max_applications_per_candidate" };
        var settings = await _context.PlatformSettings
            .Where(s => publicKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(settings);
    }
}

// ── DTOs ──
public class ModerationNoteDto { public string? Note { get; set; } }

public class AnnouncementCreateDto
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? TargetRole { get; set; }
    public bool IsBanner { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
}

public class SettingUpdateDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Description { get; set; }
}
