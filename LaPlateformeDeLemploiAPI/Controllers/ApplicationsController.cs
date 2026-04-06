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
public class ApplicationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string UserRole => User.FindFirstValue(ClaimTypes.Role)!;

    public ApplicationsController(AppDbContext context) => _context = context;

    // POST: Candidater a une offre (JobSeeker)
    [HttpPost]
    public async Task<ActionResult<ApplicationDto>> Apply(ApplicationCreateDto dto)
    {
        if (UserRole != "JobSeeker")
            return Forbid();

        var exists = await _context.Applications.AnyAsync(a => a.UserId == UserId && a.JobOfferId == dto.JobOfferId);
        if (exists) return BadRequest(new { message = "Vous avez deja postule a cette offre." });

        var job = await _context.JobOffers.Include(j => j.Company).FirstOrDefaultAsync(j => j.Id == dto.JobOfferId);
        if (job == null) return NotFound(new { message = "Offre introuvable." });

        var app = new Application
        {
            UserId = UserId,
            JobOfferId = dto.JobOfferId,
            CoverLetter = dto.CoverLetter
        };

        _context.Applications.Add(app);

        // Notification au(x) recruteur(s) de l'entreprise
        var companyUsers = await _context.Users.Where(u => u.CompanyId == job.CompanyId).ToListAsync();
        var applicant = await _context.Users.FindAsync(UserId);
        foreach (var cu in companyUsers)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = cu.Id,
                Title = "Nouvelle candidature",
                Message = $"{applicant!.FirstName} {applicant.LastName} a postule a \"{job.Title}\"",
                Type = "application",
                Link = $"/candidatures-recues"
            });
        }

        await _context.SaveChangesAsync();
        return Ok(await MapToDto(app.Id));
    }

    // GET: Mes candidatures (JobSeeker)
    [HttpGet("mine")]
    public async Task<ActionResult<List<ApplicationDto>>> GetMyApplications()
    {
        if (UserRole != "JobSeeker") return Forbid();

        var apps = await _context.Applications
            .Where(a => a.UserId == UserId)
            .Include(a => a.JobOffer).ThenInclude(j => j.Company)
            .Include(a => a.User)
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new ApplicationDto
            {
                Id = a.Id, CoverLetter = a.CoverLetter, Status = a.Status,
                AppliedAt = a.AppliedAt, UpdatedAt = a.UpdatedAt,
                UserId = a.UserId, UserFullName = a.User.FirstName + " " + a.User.LastName,
                UserEmail = a.User.Email, UserPhone = a.User.Phone,
                JobOfferId = a.JobOfferId, JobOfferTitle = a.JobOffer.Title,
                CompanyName = a.JobOffer.Company.Name, Location = a.JobOffer.Location,
                ContractType = a.JobOffer.ContractType,
                InternalNotes = a.InternalNotes
            })
            .ToListAsync();

        return Ok(apps);
    }

    // GET: Candidatures recues pour mes offres (Company)
    [HttpGet("received")]
    public async Task<ActionResult<List<ApplicationDto>>> GetReceivedApplications()
    {
        if (UserRole != "Company") return Forbid();

        var user = await _context.Users.FindAsync(UserId);
        if (user?.CompanyId == null) return Forbid();

        var apps = await _context.Applications
            .Include(a => a.JobOffer).ThenInclude(j => j.Company)
            .Include(a => a.User)
            .Where(a => a.JobOffer.CompanyId == user.CompanyId)
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new ApplicationDto
            {
                Id = a.Id, CoverLetter = a.CoverLetter, Status = a.Status,
                AppliedAt = a.AppliedAt, UpdatedAt = a.UpdatedAt,
                UserId = a.UserId, UserFullName = a.User.FirstName + " " + a.User.LastName,
                UserEmail = a.User.Email, UserPhone = a.User.Phone,
                UserSkills = a.User.Skills, UserBio = a.User.Bio,
                JobOfferId = a.JobOfferId, JobOfferTitle = a.JobOffer.Title,
                CompanyName = a.JobOffer.Company.Name, Location = a.JobOffer.Location,
                ContractType = a.JobOffer.ContractType,
                InternalNotes = a.InternalNotes
            })
            .ToListAsync();

        return Ok(apps);
    }

    // PUT: Changer le statut d'une candidature (Company)
    [HttpPut("{id}/status")]
    public async Task<ActionResult<ApplicationDto>> UpdateStatus(int id, ApplicationUpdateStatusDto dto)
    {
        if (UserRole != "Company") return Forbid();

        var user = await _context.Users.FindAsync(UserId);
        var app = await _context.Applications
            .Include(a => a.JobOffer)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (app == null) return NotFound();
        if (app.JobOffer.CompanyId != user?.CompanyId) return Forbid();

        var validStatuses = new[] { "Reviewed", "Accepted", "Rejected" };
        if (!validStatuses.Contains(dto.Status))
            return BadRequest(new { message = "Statut invalide." });

        app.Status = dto.Status;
        app.UpdatedAt = DateTime.UtcNow;

        // Notification au candidat
        var statusMsg = dto.Status switch
        {
            "Accepted" => "Felicitations ! Votre candidature a ete acceptee",
            "Rejected" => "Votre candidature n'a malheureusement pas ete retenue",
            "Reviewed" => "Votre candidature a ete consultee par le recruteur",
            _ => "Le statut de votre candidature a change"
        };
        var notifType = dto.Status == "Accepted" ? "success" : dto.Status == "Rejected" ? "warning" : "info";
        _context.Notifications.Add(new Notification
        {
            UserId = app.UserId,
            Title = $"Candidature {(dto.Status == "Accepted" ? "acceptee" : dto.Status == "Rejected" ? "refusee" : "consultee")}",
            Message = $"{statusMsg} pour \"{app.JobOffer.Title}\"",
            Type = notifType,
            Link = "/mes-candidatures"
        });

        await _context.SaveChangesAsync();
        return Ok(await MapToDto(app.Id));
    }

    // GET: Verifier si j'ai deja postule
    [HttpGet("check/{jobOfferId}")]
    public async Task<ActionResult> HasApplied(int jobOfferId)
    {
        var applied = await _context.Applications.AnyAsync(a => a.UserId == UserId && a.JobOfferId == jobOfferId);
        return Ok(new { applied });
    }

    // DELETE: Retirer ma candidature
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var app = await _context.Applications.FindAsync(id);
        if (app == null) return NotFound();
        if (app.UserId != UserId) return Forbid();

        _context.Applications.Remove(app);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // PUT: Bulk status update (Company)
    [HttpPut("bulk-status")]
    public async Task<IActionResult> BulkUpdateStatus(BulkStatusDto dto)
    {
        if (UserRole != "Company") return Forbid();
        var validStatuses = new[] { "Reviewed", "Accepted", "Rejected" };
        if (!validStatuses.Contains(dto.Status)) return BadRequest(new { message = "Statut invalide." });

        var user = await _context.Users.FindAsync(UserId);
        var apps = await _context.Applications
            .Include(a => a.JobOffer)
            .Where(a => dto.Ids.Contains(a.Id) && a.JobOffer.CompanyId == user!.CompanyId)
            .ToListAsync();

        foreach (var app in apps)
        {
            app.Status = dto.Status;
            app.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();
        return Ok(new { message = $"{apps.Count} candidature(s) mise(s) a jour.", count = apps.Count });
    }

    // PUT: Notes internes (Company)
    [HttpPut("{id}/notes")]
    public async Task<IActionResult> UpdateNotes(int id, UpdateNotesDto dto)
    {
        if (UserRole != "Company") return Forbid();
        var user = await _context.Users.FindAsync(UserId);
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (app.JobOffer.CompanyId != user?.CompanyId) return Forbid();

        app.InternalNotes = dto.Notes;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Notes enregistrees." });
    }

    // GET: Export CSV
    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv()
    {
        var apps = await GetApplicationsForExport();
        var csv = "Candidat;Email;Telephone;Offre;Entreprise;Ville;Contrat;Statut;Date;Lettre de motivation\n";
        foreach (var a in apps)
        {
            csv += $"\"{a.UserFullName}\";\"{a.UserEmail}\";\"{a.UserPhone ?? ""}\";\"{a.JobOfferTitle}\";\"{a.CompanyName}\";\"{a.Location}\";\"{a.ContractType}\";\"{a.Status}\";\"{a.AppliedAt:dd/MM/yyyy}\";\"{(a.CoverLetter ?? "").Replace("\"", "'")}\"\n";
        }
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"candidatures-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private async Task<List<ApplicationDto>> GetApplicationsForExport()
    {
        if (UserRole == "Company")
        {
            var user = await _context.Users.FindAsync(UserId);
            return await _context.Applications
                .Include(a => a.JobOffer).ThenInclude(j => j.Company)
                .Include(a => a.User)
                .Where(a => a.JobOffer.CompanyId == user!.CompanyId)
                .OrderByDescending(a => a.AppliedAt)
                .Select(a => new ApplicationDto
                {
                    Id = a.Id, CoverLetter = a.CoverLetter, Status = a.Status,
                    AppliedAt = a.AppliedAt, UpdatedAt = a.UpdatedAt,
                    UserId = a.UserId, UserFullName = a.User.FirstName + " " + a.User.LastName,
                    UserEmail = a.User.Email, UserPhone = a.User.Phone,
                    UserSkills = a.User.Skills, UserBio = a.User.Bio,
                    JobOfferId = a.JobOfferId, JobOfferTitle = a.JobOffer.Title,
                    CompanyName = a.JobOffer.Company.Name, Location = a.JobOffer.Location,
                    ContractType = a.JobOffer.ContractType,
                InternalNotes = a.InternalNotes
                }).ToListAsync();
        }
        return await _context.Applications
            .Include(a => a.JobOffer).ThenInclude(j => j.Company)
            .Include(a => a.User)
            .Where(a => a.UserId == UserId)
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new ApplicationDto
            {
                Id = a.Id, CoverLetter = a.CoverLetter, Status = a.Status,
                AppliedAt = a.AppliedAt, UpdatedAt = a.UpdatedAt,
                UserId = a.UserId, UserFullName = a.User.FirstName + " " + a.User.LastName,
                UserEmail = a.User.Email, UserPhone = a.User.Phone,
                UserSkills = a.User.Skills, UserBio = a.User.Bio,
                JobOfferId = a.JobOfferId, JobOfferTitle = a.JobOffer.Title,
                CompanyName = a.JobOffer.Company.Name, Location = a.JobOffer.Location,
                ContractType = a.JobOffer.ContractType,
                InternalNotes = a.InternalNotes
            }).ToListAsync();
    }

    private async Task<ApplicationDto> MapToDto(int id)
    {
        return await _context.Applications
            .Where(a => a.Id == id)
            .Include(a => a.JobOffer).ThenInclude(j => j.Company)
            .Include(a => a.User)
            .Select(a => new ApplicationDto
            {
                Id = a.Id, CoverLetter = a.CoverLetter, Status = a.Status,
                AppliedAt = a.AppliedAt, UpdatedAt = a.UpdatedAt,
                UserId = a.UserId, UserFullName = a.User.FirstName + " " + a.User.LastName,
                UserEmail = a.User.Email, UserPhone = a.User.Phone,
                UserSkills = a.User.Skills, UserBio = a.User.Bio,
                JobOfferId = a.JobOfferId, JobOfferTitle = a.JobOffer.Title,
                CompanyName = a.JobOffer.Company.Name, Location = a.JobOffer.Location,
                ContractType = a.JobOffer.ContractType,
                InternalNotes = a.InternalNotes
            })
            .FirstAsync();
    }
}
