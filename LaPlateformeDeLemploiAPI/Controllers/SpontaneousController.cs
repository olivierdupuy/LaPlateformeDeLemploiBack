using System.Security.Claims;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.DTOs;
using LaPlateformeDeLemploiAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LaPlateformeDeLemploiAPI.Controllers;

[ApiController]
[Route("api/spontaneous")]
[Authorize]
public class SpontaneousController : ControllerBase
{
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string UserRole => User.FindFirstValue(ClaimTypes.Role)!;

    public SpontaneousController(AppDbContext context) => _context = context;

    [HttpPost]
    public async Task<ActionResult<SpontaneousApplicationDto>> Apply(SpontaneousApplicationCreateDto dto)
    {
        if (UserRole != "JobSeeker") return Forbid();

        var company = await _context.Companies.FindAsync(dto.CompanyId);
        if (company == null) return NotFound(new { message = "Entreprise introuvable." });

        var exists = await _context.SpontaneousApplications
            .AnyAsync(s => s.UserId == UserId && s.CompanyId == dto.CompanyId);
        if (exists) return BadRequest(new { message = "Vous avez deja envoye une candidature spontanee a cette entreprise." });

        var app = new SpontaneousApplication
        {
            UserId = UserId,
            CompanyId = dto.CompanyId,
            CoverLetter = dto.CoverLetter
        };
        _context.SpontaneousApplications.Add(app);

        // Notifier l'entreprise
        var companyUser = await _context.Users.FirstOrDefaultAsync(u => u.CompanyId == dto.CompanyId);
        if (companyUser != null)
        {
            var sender = await _context.Users.FindAsync(UserId);
            _context.Notifications.Add(new Notification
            {
                UserId = companyUser.Id,
                Title = "Candidature spontanee",
                Message = $"{sender!.FirstName} {sender.LastName} vous a envoye une candidature spontanee",
                Type = "application",
                Link = "/candidatures-spontanees"
            });
        }

        await _context.SaveChangesAsync();

        return Ok(new SpontaneousApplicationDto
        {
            Id = app.Id, CoverLetter = app.CoverLetter, Status = app.Status,
            AppliedAt = app.AppliedAt, UserId = app.UserId,
            CompanyId = app.CompanyId, CompanyName = company.Name
        });
    }

    // Mes candidatures spontanees (JobSeeker)
    [HttpGet("mine")]
    public async Task<ActionResult<List<SpontaneousApplicationDto>>> GetMine()
    {
        var apps = await _context.SpontaneousApplications
            .Include(s => s.Company)
            .Where(s => s.UserId == UserId)
            .OrderByDescending(s => s.AppliedAt)
            .Select(s => new SpontaneousApplicationDto
            {
                Id = s.Id, CoverLetter = s.CoverLetter, Status = s.Status,
                AppliedAt = s.AppliedAt, UserId = s.UserId,
                CompanyId = s.CompanyId, CompanyName = s.Company.Name
            }).ToListAsync();
        return Ok(apps);
    }

    // Candidatures spontanees recues (Company)
    [HttpGet("received")]
    public async Task<ActionResult<List<SpontaneousApplicationDto>>> GetReceived()
    {
        if (UserRole != "Company") return Forbid();
        var user = await _context.Users.FindAsync(UserId);

        var apps = await _context.SpontaneousApplications
            .Include(s => s.User)
            .Include(s => s.Company)
            .Where(s => s.CompanyId == user!.CompanyId)
            .OrderByDescending(s => s.AppliedAt)
            .Select(s => new SpontaneousApplicationDto
            {
                Id = s.Id, CoverLetter = s.CoverLetter, Status = s.Status,
                AppliedAt = s.AppliedAt, UserId = s.UserId,
                UserFullName = s.User.FirstName + " " + s.User.LastName,
                UserEmail = s.User.Email, UserPhone = s.User.Phone,
                UserSkills = s.User.Skills, UserBio = s.User.Bio,
                CompanyId = s.CompanyId, CompanyName = s.Company.Name
            }).ToListAsync();
        return Ok(apps);
    }

    // Verifier si deja postule
    [HttpGet("check/{companyId}")]
    public async Task<ActionResult> HasApplied(int companyId)
    {
        var applied = await _context.SpontaneousApplications
            .AnyAsync(s => s.UserId == UserId && s.CompanyId == companyId);
        return Ok(new { applied });
    }

    // Changer statut (Company)
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, ApplicationUpdateStatusDto dto)
    {
        if (UserRole != "Company") return Forbid();
        var user = await _context.Users.FindAsync(UserId);
        var app = await _context.SpontaneousApplications.FindAsync(id);
        if (app == null) return NotFound();
        if (app.CompanyId != user!.CompanyId) return Forbid();

        app.Status = dto.Status;
        app.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Statut mis a jour." });
    }
}
