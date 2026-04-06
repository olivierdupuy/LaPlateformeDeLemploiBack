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
public class AccountController : ControllerBase
{
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public AccountController(AppDbContext context) => _context = context;

    [HttpGet("security")]
    public async Task<ActionResult<AccountSecurityDto>> GetSecurityInfo()
    {
        var user = await _context.Users.FindAsync(UserId);
        if (user == null) return NotFound();

        return Ok(new AccountSecurityDto
        {
            Email = user.Email,
            CreatedAt = user.CreatedAt,
            LastPasswordChange = user.LastPasswordChange
        });
    }

    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
            return BadRequest(new { message = "Le nouveau mot de passe doit contenir au moins 6 caracteres." });

        var user = await _context.Users.FindAsync(UserId);
        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "Le mot de passe actuel est incorrect." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.LastPasswordChange = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Mot de passe modifie avec succes." });
    }

    [HttpPut("change-email")]
    public async Task<IActionResult> ChangeEmail(ChangeEmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NewEmail) || !dto.NewEmail.Contains('@'))
            return BadRequest(new { message = "Adresse email invalide." });

        var user = await _context.Users.FindAsync(UserId);
        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return BadRequest(new { message = "Le mot de passe est incorrect." });

        if (await _context.Users.AnyAsync(u => u.Email == dto.NewEmail && u.Id != UserId))
            return BadRequest(new { message = "Cet email est deja utilise par un autre compte." });

        user.Email = dto.NewEmail;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Email modifie avec succes.", email = user.Email });
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAccount(DeleteAccountDto dto)
    {
        var user = await _context.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Id == UserId);
        if (user == null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return BadRequest(new { message = "Le mot de passe est incorrect." });

        // Supprimer les donnees liees
        var applications = _context.Applications.Where(a => a.UserId == UserId);
        _context.Applications.RemoveRange(applications);

        var favorites = _context.Favorites.Where(f => f.UserId == UserId);
        _context.Favorites.RemoveRange(favorites);

        var notifications = _context.Notifications.Where(n => n.UserId == UserId);
        _context.Notifications.RemoveRange(notifications);

        var resume = await _context.Resumes.FirstOrDefaultAsync(r => r.UserId == UserId);
        if (resume != null)
        {
            _context.ResumeExperiences.RemoveRange(_context.ResumeExperiences.Where(e => e.ResumeId == resume.Id));
            _context.ResumeEducations.RemoveRange(_context.ResumeEducations.Where(e => e.ResumeId == resume.Id));
            _context.ResumeSkills.RemoveRange(_context.ResumeSkills.Where(s => s.ResumeId == resume.Id));
            _context.ResumeLanguages.RemoveRange(_context.ResumeLanguages.Where(l => l.ResumeId == resume.Id));
            _context.Resumes.Remove(resume);
        }

        var preferences = await _context.JobPreferences.FirstOrDefaultAsync(p => p.UserId == UserId);
        if (preferences != null)
            _context.JobPreferences.Remove(preferences);

        // Si entreprise, supprimer les offres et candidatures associees
        if (user.Role == "Company" && user.CompanyId.HasValue)
        {
            var jobOfferIds = await _context.JobOffers
                .Where(j => j.CompanyId == user.CompanyId)
                .Select(j => j.Id)
                .ToListAsync();

            var jobApplications = _context.Applications.Where(a => jobOfferIds.Contains(a.JobOfferId));
            _context.Applications.RemoveRange(jobApplications);

            var jobFavorites = _context.Favorites.Where(f => jobOfferIds.Contains(f.JobOfferId));
            _context.Favorites.RemoveRange(jobFavorites);

            var jobOffers = _context.JobOffers.Where(j => j.CompanyId == user.CompanyId);
            _context.JobOffers.RemoveRange(jobOffers);

            _context.Companies.Remove(user.Company!);
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Compte supprime avec succes." });
    }
}
