using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.DTOs;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InterviewsController : ControllerBase
{
    private readonly AppDbContext _context;
    public InterviewsController(AppDbContext context) => _context = context;
    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var userId = GetUserId();
        var query = _context.Interviews
            .Include(i => i.Application).ThenInclude(a => a.JobOffer)
            .Include(i => i.Application).ThenInclude(a => a.User)
            .AsQueryable();

        if (!IsAdmin())
        {
            query = query.Where(i =>
                i.Application.UserId == userId ||
                i.Application.JobOffer.CreatedByUserId == userId);
        }

        var interviews = await query.OrderByDescending(i => i.ProposedAt).ToListAsync();

        return Ok(interviews.Select(i => new {
            i.Id, i.ApplicationId, i.ProposedAt, i.Location, i.Notes, i.Status, i.CreatedAt,
            i.Duration, i.Type, i.InterviewerName, i.CandidateSlots, i.CandidateMessage,
            candidateName = i.Application.FullName,
            candidateId = i.Application.UserId,
            jobTitle = i.Application.JobOffer.Title,
            company = i.Application.JobOffer.Company
        }));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult> Create(InterviewCreateDto dto)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == dto.ApplicationId);
        if (app == null) return NotFound();
        if (!IsAdmin() && app.JobOffer.CreatedByUserId != GetUserId()) return Forbid();

        var interview = new Interview
        {
            ApplicationId = dto.ApplicationId,
            ProposedAt = dto.ProposedAt,
            Location = dto.Location,
            Notes = dto.Notes,
            Duration = dto.Duration,
            Type = dto.Type,
            InterviewerName = dto.InterviewerName
        };

        _context.Interviews.Add(interview);
        await _context.SaveChangesAsync();

        // Notification candidat
        if (app.UserId != null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = app.UserId,
                Title = "Entretien propose",
                Message = $"Un entretien a ete propose pour \"{app.JobOffer.Title}\" chez {app.JobOffer.Company} le {dto.ProposedAt:dd/MM/yyyy a HH:mm}.",
                Link = "/entretiens",
                Type = "Entretien"
            });
            await _context.SaveChangesAsync();
        }

        return Ok(interview);
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, InterviewUpdateStatusDto dto)
    {
        var interview = await _context.Interviews
            .Include(i => i.Application).ThenInclude(a => a.JobOffer)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (interview == null) return NotFound();

        var userId = GetUserId();
        var isCandidate = interview.Application.UserId == userId;
        var isRecruiter = interview.Application.JobOffer.CreatedByUserId == userId;
        if (!IsAdmin() && !isCandidate && !isRecruiter) return Forbid();

        var valid = new[] { "Proposed", "Accepted", "Declined", "Completed", "Cancelled", "Negotiating" };
        if (!valid.Contains(dto.Status)) return BadRequest("Statut invalide.");

        interview.Status = dto.Status;
        await _context.SaveChangesAsync();

        // Notify the other party
        var statusLabels = new Dictionary<string, string>
        {
            {"Accepted", "accepte"}, {"Declined", "decline"}, {"Completed", "termine"}, {"Cancelled", "annule"}
        };
        var targetUserId = isCandidate ? interview.Application.JobOffer.CreatedByUserId : interview.Application.UserId;
        if (targetUserId != null && statusLabels.ContainsKey(dto.Status))
        {
            _context.Notifications.Add(new Notification
            {
                UserId = targetUserId,
                Title = "Mise a jour entretien",
                Message = $"L'entretien pour \"{interview.Application.JobOffer.Title}\" a ete {statusLabels[dto.Status]}.",
                Link = "/entretiens",
                Type = "Entretien"
            });
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Delete(int id)
    {
        var interview = await _context.Interviews.Include(i => i.Application).ThenInclude(a => a.JobOffer).FirstOrDefaultAsync(i => i.Id == id);
        if (interview == null) return NotFound();
        if (!IsAdmin() && interview.Application.JobOffer.CreatedByUserId != GetUserId()) return Forbid();
        _context.Interviews.Remove(interview);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
