using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.DTOs;
using lpdeBack.Hubs;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApplicationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly PushNotificationService _pushService;

    public ApplicationsController(AppDbContext context, IHubContext<ChatHub> hubContext, PushNotificationService pushService)
    {
        _context = context;
        _hubContext = hubContext;
        _pushService = pushService;
    }

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin() => User.IsInRole("Admin");

    // ── Recruteur/Admin ──

    [HttpGet]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<IEnumerable<Application>>> GetAll()
    {
        var userId = GetUserId();
        var query = _context.Applications.Include(a => a.JobOffer).AsQueryable();
        if (!IsAdmin())
            query = query.Where(a => a.JobOffer.CreatedByUserId == userId);
        return await query.OrderByDescending(a => a.AppliedAt).ToListAsync();
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<Application>> GetById(int id)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (!IsAdmin() && app.JobOffer.CreatedByUserId != GetUserId()) return Forbid();
        return app;
    }

    [HttpGet("job/{jobOfferId}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<IEnumerable<Application>>> GetByJobOffer(int jobOfferId)
    {
        var job = await _context.JobOffers.FindAsync(jobOfferId);
        if (job == null) return NotFound();
        if (!IsAdmin() && job.CreatedByUserId != GetUserId()) return Forbid();
        return await _context.Applications.Where(a => a.JobOfferId == jobOfferId).OrderByDescending(a => a.AppliedAt).ToListAsync();
    }

    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> UpdateStatus(int id, ApplicationUpdateStatusDto dto)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (!IsAdmin() && app.JobOffer.CreatedByUserId != GetUserId()) return Forbid();

        var validStatuses = new[] { "Pending", "Reviewed", "Accepted", "Rejected" };
        if (!validStatuses.Contains(dto.Status)) return BadRequest("Statut invalide.");

        var statusLabels = new Dictionary<string, string>
        {
            {"Pending", "en attente"}, {"Reviewed", "examinee"}, {"Accepted", "acceptee"}, {"Rejected", "refusee"}
        };

        app.Status = dto.Status;
        await _context.SaveChangesAsync();

        // Notification au candidat
        if (app.UserId != null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = app.UserId,
                Title = "Statut de candidature modifie",
                Message = $"Votre candidature pour \"{app.JobOffer.Title}\" chez {app.JobOffer.Company} est maintenant {statusLabels.GetValueOrDefault(dto.Status, dto.Status)}.",
                Link = "/suivi",
                Type = "StatutModifie"
            });
            await _context.SaveChangesAsync();

            // Real-time notification to candidate
            foreach (var connId in ChatHub.GetConnectionIds(app.UserId))
            {
                await _hubContext.Clients.Client(connId).SendAsync("ApplicationStatusChanged", new
                {
                    applicationId = app.Id,
                    status = dto.Status,
                    jobTitle = app.JobOffer.Title,
                    company = app.JobOffer.Company
                });
                await _hubContext.Clients.Client(connId).SendAsync("NewNotification");
            }

            // Push notification (mobile will suppress if app is in foreground)
            await _pushService.SendToUser(app.UserId, "Statut de candidature modifie",
                $"Votre candidature pour \"{app.JobOffer.Title}\" est maintenant {statusLabels.GetValueOrDefault(dto.Status, dto.Status)}.",
                "/tabs/applications");
        }

        return NoContent();
    }

    [HttpPatch("{id}/notes")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> UpdateNotes(int id, ApplicationUpdateNotesDto dto)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (!IsAdmin() && app.JobOffer.CreatedByUserId != GetUserId()) return Forbid();

        app.RecruiterNotes = dto.Notes;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Delete(int id)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (!IsAdmin() && app.JobOffer.CreatedByUserId != GetUserId()) return Forbid();

        _context.Applications.Remove(app);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Stats pour le recruteur connecte (ses offres uniquement)</summary>
    [HttpGet("stats/recruiter")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<object>> RecruiterStats()
    {
        var userId = GetUserId();
        var myOffers = _context.JobOffers.Where(j => j.CreatedByUserId == userId);
        var myOfferIds = myOffers.Select(j => j.Id);
        var myApps = _context.Applications.Where(a => myOfferIds.Contains(a.JobOfferId));

        var totalOffres = await myOffers.CountAsync();
        var offresActives = await myOffers.CountAsync(j => j.IsActive);
        var offresExpirees = await myOffers.CountAsync(j => !j.IsActive);
        var totalCandidatures = await myApps.CountAsync();
        var enAttente = await myApps.CountAsync(a => a.Status == "Pending");
        var examinees = await myApps.CountAsync(a => a.Status == "Reviewed");
        var acceptees = await myApps.CountAsync(a => a.Status == "Accepted");
        var refusees = await myApps.CountAsync(a => a.Status == "Rejected");
        var entretiensPlanifies = await _context.Interviews.CountAsync(i => myOfferIds.Contains(i.Application.JobOfferId) && (i.Status == "Proposed" || i.Status == "Accepted"));
        var messagesNonLus = await _context.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead);

        var candidaturesParOffre = await myOffers
            .Select(j => new { label = j.Title.Length > 30 ? j.Title.Substring(0, 30) + "..." : j.Title, value = j.Applications.Count })
            .OrderByDescending(x => x.value)
            .Take(6)
            .ToListAsync();

        var candidaturesParStatut = new[] {
            new { label = "En attente", value = enAttente },
            new { label = "Examinees", value = examinees },
            new { label = "Acceptees", value = acceptees },
            new { label = "Refusees", value = refusees }
        };

        return new {
            totalOffres, offresActives, offresExpirees, totalCandidatures,
            enAttente, examinees, acceptees, refusees,
            entretiensPlanifies, messagesNonLus,
            candidaturesParOffre, candidaturesParStatut
        };
    }

    /// <summary>Stats pour le candidat connecte</summary>
    [HttpGet("stats/candidate")]
    [Authorize]
    public async Task<ActionResult<object>> CandidateStats()
    {
        var userId = GetUserId();
        var myApps = _context.Applications.Where(a => a.UserId == userId);

        var totalCandidatures = await myApps.CountAsync();
        var enAttente = await myApps.CountAsync(a => a.Status == "Pending");
        var examinees = await myApps.CountAsync(a => a.Status == "Reviewed");
        var acceptees = await myApps.CountAsync(a => a.Status == "Accepted");
        var refusees = await myApps.CountAsync(a => a.Status == "Rejected");
        var entretiens = await _context.Interviews.CountAsync(i => i.Application.UserId == userId && (i.Status == "Proposed" || i.Status == "Accepted"));
        var messagesNonLus = await _context.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead);
        var favoris = 0; // client-side only
        var recherches = await _context.SavedSearches.CountAsync(s => s.UserId == userId);

        var candidaturesParStatut = new[] {
            new { label = "En attente", value = enAttente },
            new { label = "Examinees", value = examinees },
            new { label = "Acceptees", value = acceptees },
            new { label = "Refusees", value = refusees }
        };

        var dernieresCandidatures = await myApps
            .Include(a => a.JobOffer)
            .OrderByDescending(a => a.AppliedAt)
            .Take(5)
            .Select(a => new { a.Id, a.JobOfferId, titre = a.JobOffer.Title, entreprise = a.JobOffer.Company, a.Status, a.AppliedAt })
            .ToListAsync();

        return new {
            totalCandidatures, enAttente, examinees, acceptees, refusees,
            entretiens, messagesNonLus, recherches,
            candidaturesParStatut, dernieresCandidatures
        };
    }

    // ── Candidat ──

    [HttpGet("track")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<object>>> TrackMyApplications()
    {
        var userId = GetUserId();
        var apps = await _context.Applications
            .Include(a => a.JobOffer)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new {
                a.Id, a.JobOfferId, a.FullName, a.Email, a.Phone, a.CoverLetter,
                a.ResumeUrl, a.Status, a.AppliedAt, a.UserId,
                // Exclure RecruiterNotes pour les candidats
                JobOffer = a.JobOffer
            })
            .ToListAsync();
        return Ok(apps);
    }

    [HttpPost]
    [Authorize(Roles = "Candidate")]
    public async Task<ActionResult<Application>> Create(ApplicationCreateDto dto)
    {
        var userId = GetUserId()!;
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Unauthorized();

        var job = await _context.JobOffers.FirstOrDefaultAsync(j => j.Id == dto.JobOfferId && j.IsActive);
        if (job == null) return BadRequest("Offre introuvable ou inactive.");

        var alreadyApplied = await _context.Applications.AnyAsync(a => a.JobOfferId == dto.JobOfferId && a.UserId == userId);
        if (alreadyApplied) return BadRequest("Vous avez deja postule a cette offre.");

        // Check max applications limit
        var maxAppsStr = await _context.PlatformSettings
            .Where(s => s.Key == "max_applications_per_candidate")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        if (int.TryParse(maxAppsStr, out var maxApps) && maxApps > 0)
        {
            var currentCount = await _context.Applications.CountAsync(a => a.UserId == userId);
            if (currentCount >= maxApps)
                return BadRequest($"Vous avez atteint la limite de {maxApps} candidatures. Veuillez attendre qu'une de vos candidatures soit traitee.");
        }

        var app = new Application
        {
            JobOfferId = dto.JobOfferId,
            FullName = $"{user.FirstName} {user.LastName}",
            Email = user.Email!,
            Phone = dto.Phone ?? user.PhoneNumber,
            CoverLetter = dto.CoverLetter,
            ResumeUrl = user.ResumeUrl,
            UserId = userId
        };

        _context.Applications.Add(app);
        await _context.SaveChangesAsync();

        // Notification au recruteur
        if (job.CreatedByUserId != null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = job.CreatedByUserId,
                Title = "Nouvelle candidature",
                Message = $"{user.FirstName} {user.LastName} a postule a votre offre \"{job.Title}\".",
                Link = "/admin/candidatures",
                Type = "NouveauCandidat"
            });
            await _context.SaveChangesAsync();

            // Real-time notification to recruiter
            foreach (var connId in ChatHub.GetConnectionIds(job.CreatedByUserId))
            {
                await _hubContext.Clients.Client(connId).SendAsync("NewApplication", new
                {
                    applicationId = app.Id,
                    candidateName = $"{user.FirstName} {user.LastName}",
                    jobTitle = job.Title
                });
                await _hubContext.Clients.Client(connId).SendAsync("NewNotification");
            }

            // Push notification to recruiter
            await _pushService.SendToUser(job.CreatedByUserId, "Nouvelle candidature",
                $"{user.FirstName} {user.LastName} a postule a \"{job.Title}\"",
                "/tabs/recruiter-applications");
        }

        return CreatedAtAction(nameof(GetById), new { id = app.Id }, app);
    }
}
