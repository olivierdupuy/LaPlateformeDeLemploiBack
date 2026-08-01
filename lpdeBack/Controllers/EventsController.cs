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
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _context;
    public EventsController(AppDbContext context) => _context = context;

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin() => User.IsInRole("Admin");

    /// <summary>Liste des événements à venir (public). past=true pour l'historique.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<JobEvent>>> GetAll([FromQuery] bool past = false)
    {
        var now = DateTime.UtcNow.Date;
        var query = _context.JobEvents.AsQueryable();
        query = past
            ? query.Where(e => e.StartsAt < now).OrderByDescending(e => e.StartsAt)
            : query.Where(e => e.StartsAt >= now).OrderBy(e => e.StartsAt);
        return await query.ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<JobEvent>> GetById(int id)
    {
        var ev = await _context.JobEvents.FindAsync(id);
        return ev == null ? NotFound() : ev;
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<JobEvent>> Create(JobEventDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title) || dto.StartsAt == default)
            return BadRequest(new { message = "Titre et date de début requis." });

        var ev = new JobEvent
        {
            Title = dto.Title, Description = dto.Description ?? "", Type = string.IsNullOrWhiteSpace(dto.Type) ? "Salon" : dto.Type,
            StartsAt = dto.StartsAt, EndsAt = dto.EndsAt, IsOnline = dto.IsOnline,
            Location = dto.Location, Url = dto.Url, Organizer = dto.Organizer,
            CreatedByUserId = GetUserId(),
        };
        _context.JobEvents.Add(ev);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = ev.Id }, ev);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Delete(int id)
    {
        var ev = await _context.JobEvents.FindAsync(id);
        if (ev == null) return NotFound();
        if (!IsAdmin() && ev.CreatedByUserId != GetUserId()) return Forbid();
        _context.JobEvents.Remove(ev);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

public class JobEventDto
{
    [Required(ErrorMessage = "Donnez un titre à l'événement.")]
    [StringLength(Limites.Ligne, MinimumLength = 2)]
    [SansBalisage]
    public string Title { get; set; } = string.Empty;

    [Longueur(Limites.Texte)]
    public string? Description { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Type { get; set; }

    [Required(ErrorMessage = "Indiquez la date de début.")]
    public DateTime StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }
    public bool IsOnline { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Location { get; set; }

    [AdresseWeb(ExterneSeulement = true)]
    public string? Url { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Organizer { get; set; }
}
