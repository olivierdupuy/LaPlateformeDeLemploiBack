using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/candidate")]
[Authorize]
public class CandidateFeaturesController : ControllerBase
{
    private readonly AppDbContext _context;
    public CandidateFeaturesController(AppDbContext context) => _context = context;
    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ═══════════════════════════════════
    //  1. RETRAIT DE CANDIDATURE
    // ═══════════════════════════════════

    [HttpDelete("applications/{id}/withdraw")]
    public async Task<IActionResult> WithdrawApplication(int id)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (app.UserId != UserId()) return Forbid();
        if (app.Status == "Accepted") return BadRequest("Impossible de retirer une candidature acceptee.");

        // Notify recruiter
        if (app.JobOffer.CreatedByUserId != null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = app.JobOffer.CreatedByUserId,
                Title = "Candidature retiree",
                Message = $"{app.FullName} a retire sa candidature pour \"{app.JobOffer.Title}\".",
                Link = "/admin/candidatures",
                Type = "CandidatureRetiree"
            });
        }

        _context.Applications.Remove(app);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══════════════════════════════════
    //  2. RECOMMANDATIONS IA (matching)
    // ═══════════════════════════════════

    [HttpGet("recommendations")]
    public async Task<ActionResult<IEnumerable<object>>> GetRecommendations()
    {
        var user = await _context.Users.FindAsync(UserId());
        if (user == null) return NotFound();

        var userSkills = (user.Skills ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLower())
            .Where(s => s.Length > 1)
            .ToHashSet();

        if (userSkills.Count == 0)
            return Ok(Array.Empty<object>());

        // Get active approved offers
        var offers = await _context.JobOffers
            .Where(j => j.IsActive && j.ModerationStatus == "Approved")
            .OrderByDescending(j => j.CreatedAt)
            .Take(200)
            .ToListAsync();

        // Already applied
        var appliedIds = (await _context.Applications
            .Where(a => a.UserId == UserId())
            .Select(a => a.JobOfferId)
            .ToListAsync()).ToHashSet();

        var scored = offers
            .Where(j => !appliedIds.Contains(j.Id))
            .Select(j =>
            {
                var jobSkills = ((j.Tags ?? "") + "," + (j.Category ?? ""))
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToLower())
                    .Where(s => s.Length > 1)
                    .ToHashSet();

                // Also match title words
                var titleWords = j.Title.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 2);
                foreach (var w in titleWords) jobSkills.Add(w);

                // Location bonus
                var locationMatch = !string.IsNullOrEmpty(user.City) &&
                    j.Location.Contains(user.City, StringComparison.OrdinalIgnoreCase);

                var matched = userSkills.Intersect(jobSkills).Count();
                var total = Math.Max(userSkills.Count, 1);
                var score = (int)Math.Round((double)matched / total * 100);

                // Bonus for location match
                if (locationMatch && score > 0) score = Math.Min(score + 15, 100);
                // Bonus for remote if user has no city
                if (j.IsRemote && string.IsNullOrEmpty(user.City)) score = Math.Min(score + 10, 100);

                return new { job = j, score, matched };
            })
            .Where(x => x.score >= 20)
            .OrderByDescending(x => x.score)
            .Take(10)
            .Select(x => new
            {
                x.job.Id, x.job.Title, x.job.Company, x.job.Location,
                x.job.ContractType, x.job.Category, x.job.IsRemote,
                x.job.Salary, x.job.Tags, x.job.CreatedAt, x.job.IsUrgent,
                x.score, x.matched,
            })
            .ToList();

        return Ok(scored);
    }

    // ═══════════════════════════════════
    //  3. NOTES SUR LES OFFRES
    // ═══════════════════════════════════

    [HttpGet("notes")]
    public async Task<ActionResult<IEnumerable<JobNote>>> GetMyNotes()
    {
        return await _context.JobNotes
            .Where(n => n.UserId == UserId())
            .Include(n => n.JobOffer)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();
    }

    [HttpGet("notes/{jobId}")]
    public async Task<ActionResult<object>> GetNote(int jobId)
    {
        var note = await _context.JobNotes.FirstOrDefaultAsync(n => n.UserId == UserId() && n.JobOfferId == jobId);
        return note != null ? new { note.Id, note.Content, note.UpdatedAt } : new { Id = 0, Content = "", UpdatedAt = DateTime.MinValue };
    }

    [HttpPut("notes/{jobId}")]
    public async Task<IActionResult> SaveNote(int jobId, [FromBody] NoteDto dto)
    {
        var uid = UserId();
        var note = await _context.JobNotes.FirstOrDefaultAsync(n => n.UserId == uid && n.JobOfferId == jobId);

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            if (note != null) { _context.JobNotes.Remove(note); await _context.SaveChangesAsync(); }
            return NoContent();
        }

        if (note != null)
        {
            note.Content = dto.Content;
            note.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.JobNotes.Add(new JobNote { UserId = uid, JobOfferId = jobId, Content = dto.Content });
        }
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══════════════════════════════════
    //  4. PROPOSITION DE CRENEAUX
    // ═══════════════════════════════════

    [HttpPatch("interviews/{id}/propose-slots")]
    public async Task<IActionResult> ProposeSlots(int id, [FromBody] ProposeSlotsDto dto)
    {
        var interview = await _context.Interviews
            .Include(i => i.Application).ThenInclude(a => a.JobOffer)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (interview == null) return NotFound();
        if (interview.Application.UserId != UserId()) return Forbid();
        if (interview.Status != "Proposed") return BadRequest("Vous ne pouvez proposer des creneaux que pour un entretien en attente.");

        interview.CandidateSlots = System.Text.Json.JsonSerializer.Serialize(dto.Slots);
        interview.CandidateMessage = dto.Message;
        interview.Status = "Negotiating";
        await _context.SaveChangesAsync();

        // Notify recruiter
        if (interview.Application.JobOffer.CreatedByUserId != null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = interview.Application.JobOffer.CreatedByUserId,
                Title = "Creneaux proposes",
                Message = $"{interview.Application.FullName} a propose des creneaux alternatifs pour l'entretien \"{interview.Application.JobOffer.Title}\".",
                Link = "/entretiens",
                Type = "Entretien"
            });
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    // ═══════════════════════════════════
    //  5. DASHBOARD ANALYTIQUE ENRICHI
    // ═══════════════════════════════════

    [HttpGet("analytics")]
    public async Task<ActionResult<object>> GetAnalytics()
    {
        var uid = UserId();
        var apps = await _context.Applications
            .Where(a => a.UserId == uid)
            .Include(a => a.JobOffer)
            .ToListAsync();

        var total = apps.Count;
        var withResponse = apps.Count(a => a.Status != "Pending");
        var responseRate = total > 0 ? Math.Round((double)withResponse / total * 100) : 0;

        // Average response time (days between AppliedAt and first status change)
        // We approximate: reviewed/accepted/rejected apps have been responded to
        var respondedApps = apps.Where(a => a.Status != "Pending").ToList();

        // Applications by month (last 6 months)
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var appsByMonth = apps
            .Where(a => a.AppliedAt >= sixMonthsAgo)
            .GroupBy(a => new { a.AppliedAt.Year, a.AppliedAt.Month })
            .Select(g => new { label = $"{g.Key.Month:D2}/{g.Key.Year}", value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // Applications by category
        var appsByCategory = apps
            .GroupBy(a => a.JobOffer?.Category ?? "Autre")
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Applications by contract type
        var appsByContract = apps
            .GroupBy(a => a.JobOffer?.ContractType ?? "Autre")
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Status breakdown
        var statusBreakdown = apps
            .GroupBy(a => a.Status)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToList();

        // Interviews count
        var interviewCount = await _context.Interviews
            .CountAsync(i => i.Application.UserId == uid);

        var acceptedCount = apps.Count(a => a.Status == "Accepted");
        var conversionRate = total > 0 ? Math.Round((double)acceptedCount / total * 100) : 0;

        return new
        {
            total, responseRate, conversionRate, interviewCount,
            appsByMonth, appsByCategory, appsByContract, statusBreakdown,
        };
    }

    // ═══════════════════════════════════
    //  6. ALERTES EMPLOI
    // ═══════════════════════════════════

    [HttpPatch("saved-searches/{id}/toggle-alert")]
    public async Task<IActionResult> ToggleAlert(int id)
    {
        var search = await _context.SavedSearches.FindAsync(id);
        if (search == null || search.UserId != UserId()) return NotFound();
        search.AlertEnabled = !search.AlertEnabled;
        if (search.AlertEnabled) search.LastAlertAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { search.Id, search.AlertEnabled });
    }

    [HttpGet("check-alerts")]
    public async Task<ActionResult<object>> CheckAlerts()
    {
        var uid = UserId();
        var searches = await _context.SavedSearches
            .Where(s => s.UserId == uid && s.AlertEnabled)
            .ToListAsync();

        var newOffers = new List<object>();
        foreach (var s in searches)
        {
            var since = s.LastAlertAt ?? s.CreatedAt;
            var query = _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved" && j.CreatedAt > since);
            if (!string.IsNullOrEmpty(s.Query)) query = query.Where(j => j.Title.Contains(s.Query) || j.Company.Contains(s.Query));
            if (!string.IsNullOrEmpty(s.Category)) query = query.Where(j => j.Category == s.Category);
            if (!string.IsNullOrEmpty(s.ContractType)) query = query.Where(j => j.ContractType == s.ContractType);
            if (s.IsRemote.HasValue) query = query.Where(j => j.IsRemote == s.IsRemote.Value);
            if (!string.IsNullOrEmpty(s.Location)) query = query.Where(j => j.Location.Contains(s.Location));

            var count = await query.CountAsync();
            if (count > 0)
            {
                newOffers.Add(new { searchId = s.Id, searchLabel = s.Label, newCount = count });
                s.LastAlertAt = DateTime.UtcNow;
            }
        }
        await _context.SaveChangesAsync();
        return new { alerts = newOffers };
    }
}

// DTOs
public class NoteDto { public string? Content { get; set; } }
public class ProposeSlotsDto
{
    public List<string> Slots { get; set; } = new(); // ISO date strings
    public string? Message { get; set; }
}
