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
public class InterviewsController : ControllerBase
{
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public InterviewsController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<List<InterviewDto>>> GetAll()
    {
        var userId = UserId;
        var interviews = await _context.Interviews
            .Include(i => i.Application).ThenInclude(a => a.JobOffer)
            .Include(i => i.CandidateUser)
            .Include(i => i.CompanyUser)
            .Where(i => i.CompanyUserId == userId || i.CandidateUserId == userId)
            .OrderBy(i => i.ScheduledAt)
            .Select(i => new InterviewDto
            {
                Id = i.Id,
                ScheduledAt = i.ScheduledAt,
                DurationMinutes = i.DurationMinutes,
                Type = i.Type,
                Location = i.Location,
                Notes = i.Notes,
                Status = i.Status,
                ApplicationId = i.ApplicationId,
                JobOfferTitle = i.Application.JobOffer.Title,
                CandidateName = i.CandidateUser.FirstName + " " + i.CandidateUser.LastName,
                CandidateEmail = i.CandidateUser.Email,
                CompanyUserName = i.CompanyUser.FirstName + " " + i.CompanyUser.LastName,
                CreatedAt = i.CreatedAt
            }).ToListAsync();

        return Ok(interviews);
    }

    [HttpPost]
    public async Task<ActionResult<InterviewDto>> Create(CreateInterviewDto dto)
    {
        var userId = UserId;
        var app = await _context.Applications
            .Include(a => a.JobOffer)
            .FirstOrDefaultAsync(a => a.Id == dto.ApplicationId);

        if (app == null) return NotFound(new { message = "Candidature introuvable." });

        var user = await _context.Users.FindAsync(userId);
        if (user?.CompanyId == null || app.JobOffer.CompanyId != user.CompanyId)
            return Forbid();

        if (dto.ScheduledAt < DateTime.UtcNow)
            return BadRequest(new { message = "La date doit etre dans le futur." });

        var interview = new Interview
        {
            ApplicationId = dto.ApplicationId,
            ScheduledAt = dto.ScheduledAt,
            DurationMinutes = dto.DurationMinutes,
            Type = dto.Type,
            Location = dto.Location,
            Notes = dto.Notes,
            CompanyUserId = userId,
            CandidateUserId = app.UserId
        };

        _context.Interviews.Add(interview);

        // Notifier le candidat
        _context.Notifications.Add(new Notification
        {
            UserId = app.UserId,
            Title = "Entretien planifie",
            Message = $"Un entretien a ete planifie pour \"{app.JobOffer.Title}\" le {dto.ScheduledAt:dd/MM/yyyy a HH:mm}",
            Type = "success",
            Link = "/entretiens"
        });

        await _context.SaveChangesAsync();

        return Ok(new InterviewDto
        {
            Id = interview.Id, ScheduledAt = interview.ScheduledAt,
            DurationMinutes = interview.DurationMinutes, Type = interview.Type,
            Location = interview.Location, Notes = interview.Notes,
            Status = interview.Status, ApplicationId = interview.ApplicationId,
            JobOfferTitle = app.JobOffer.Title,
            CandidateName = (await _context.Users.FindAsync(app.UserId))!.FirstName + " " + (await _context.Users.FindAsync(app.UserId))!.LastName,
            CandidateEmail = (await _context.Users.FindAsync(app.UserId))!.Email,
            CompanyUserName = user.FirstName + " " + user.LastName,
            CreatedAt = interview.CreatedAt
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateInterviewDto dto)
    {
        var interview = await _context.Interviews.FindAsync(id);
        if (interview == null) return NotFound();

        var userId = UserId;
        if (interview.CompanyUserId != userId && interview.CandidateUserId != userId) return Forbid();

        if (dto.ScheduledAt.HasValue) interview.ScheduledAt = dto.ScheduledAt.Value;
        if (dto.DurationMinutes.HasValue) interview.DurationMinutes = dto.DurationMinutes.Value;
        if (dto.Type != null) interview.Type = dto.Type;
        if (dto.Location != null) interview.Location = dto.Location;
        if (dto.Notes != null) interview.Notes = dto.Notes;
        if (dto.Status != null) interview.Status = dto.Status;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Entretien mis a jour." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var interview = await _context.Interviews.FindAsync(id);
        if (interview == null) return NotFound();
        if (interview.CompanyUserId != UserId) return Forbid();

        _context.Interviews.Remove(interview);

        _context.Notifications.Add(new Notification
        {
            UserId = interview.CandidateUserId,
            Title = "Entretien annule",
            Message = "Un entretien a ete annule par le recruteur",
            Type = "warning",
            Link = "/entretiens"
        });

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
