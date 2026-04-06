using System.Security.Claims;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.DTOs;
using LaPlateformeDeLemploiAPI.Models;
using LaPlateformeDeLemploiAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LaPlateformeDeLemploiAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ResumeController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ResumeParserService _parser;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public ResumeController(AppDbContext context, ResumeParserService parser)
    {
        _context = context;
        _parser = parser;
    }

    [HttpGet]
    public async Task<ActionResult<ResumeDto>> GetMyResume()
    {
        var resume = await LoadResume(UserId);
        if (resume == null) return Ok((ResumeDto?)null);
        return Ok(MapToDto(resume));
    }

    // Accessible par les recruteurs aussi
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<ResumeDto>> GetByUser(int userId)
    {
        var resume = await LoadResume(userId);
        if (resume == null) return NotFound(new { message = "Aucun CV trouve." });
        return Ok(MapToDto(resume));
    }

    [HttpPost]
    public async Task<ActionResult<ResumeDto>> Save(ResumeSaveDto dto)
    {
        var existing = await LoadResume(UserId);

        if (existing != null)
        {
            // Update
            existing.Title = dto.Title;
            existing.Summary = dto.Summary;
            existing.UpdatedAt = DateTime.UtcNow;

            // Replace collections
            _context.ResumeExperiences.RemoveRange(existing.Experiences);
            _context.ResumeEducations.RemoveRange(existing.Educations);
            _context.ResumeSkills.RemoveRange(existing.Skills);
            _context.ResumeLanguages.RemoveRange(existing.Languages);

            existing.Experiences = dto.Experiences.Select((e, i) => new ResumeExperience
            {
                JobTitle = e.JobTitle, Company = e.Company, Location = e.Location,
                StartDate = e.StartDate, EndDate = e.EndDate, IsCurrent = e.IsCurrent,
                Description = e.Description, Order = i, ResumeId = existing.Id
            }).ToList();

            existing.Educations = dto.Educations.Select((e, i) => new ResumeEducation
            {
                Degree = e.Degree, School = e.School, Location = e.Location,
                StartDate = e.StartDate, EndDate = e.EndDate, IsCurrent = e.IsCurrent,
                Description = e.Description, Order = i, ResumeId = existing.Id
            }).ToList();

            existing.Skills = dto.Skills.Select((s, i) => new ResumeSkill
            {
                Name = s.Name, Level = s.Level, Order = i, ResumeId = existing.Id
            }).ToList();

            existing.Languages = dto.Languages.Select((l, i) => new ResumeLanguage
            {
                Name = l.Name, Level = l.Level, Order = i, ResumeId = existing.Id
            }).ToList();

            await _context.SaveChangesAsync();
            var updated = await LoadResume(UserId);
            return Ok(MapToDto(updated!));
        }
        else
        {
            // Create
            var resume = new Resume
            {
                UserId = UserId,
                Title = dto.Title,
                Summary = dto.Summary,
                Experiences = dto.Experiences.Select((e, i) => new ResumeExperience
                {
                    JobTitle = e.JobTitle, Company = e.Company, Location = e.Location,
                    StartDate = e.StartDate, EndDate = e.EndDate, IsCurrent = e.IsCurrent,
                    Description = e.Description, Order = i
                }).ToList(),
                Educations = dto.Educations.Select((e, i) => new ResumeEducation
                {
                    Degree = e.Degree, School = e.School, Location = e.Location,
                    StartDate = e.StartDate, EndDate = e.EndDate, IsCurrent = e.IsCurrent,
                    Description = e.Description, Order = i
                }).ToList(),
                Skills = dto.Skills.Select((s, i) => new ResumeSkill
                {
                    Name = s.Name, Level = s.Level, Order = i
                }).ToList(),
                Languages = dto.Languages.Select((l, i) => new ResumeLanguage
                {
                    Name = l.Name, Level = l.Level, Order = i
                }).ToList()
            };

            _context.Resumes.Add(resume);
            await _context.SaveChangesAsync();
            var created = await LoadResume(UserId);
            return Ok(MapToDto(created!));
        }
    }

    // Upload + parsing IA d'un CV (PDF, DOCX, TXT)
    [HttpPost("parse")]
    [RequestSizeLimit(10_000_000)] // 10 Mo max
    public async Task<ActionResult<ResumeSaveDto>> ParseFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Aucun fichier envoye." });

        var allowedExts = new[] { ".pdf", ".docx", ".doc", ".txt" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExts.Contains(ext))
            return BadRequest(new { message = $"Format non supporte. Formats acceptes : {string.Join(", ", allowedExts)}" });

        try
        {
            // 1. Extraire le texte du fichier
            using var stream = file.OpenReadStream();
            var rawText = _parser.ExtractText(stream, file.FileName);

            if (string.IsNullOrWhiteSpace(rawText))
                return BadRequest(new { message = "Impossible d'extraire le texte du fichier. Verifiez que le fichier n'est pas vide ou protege." });

            // 2. Analyser avec l'IA
            var parsed = await _parser.ParseWithAI(rawText);
            if (parsed == null)
                return BadRequest(new { message = "L'IA n'a pas pu analyser le CV. Reessayez ou remplissez manuellement." });

            return Ok(parsed);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Erreur lors de l'analyse du fichier. Verifiez le format et reessayez." });
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete()
    {
        var resume = await _context.Resumes.FirstOrDefaultAsync(r => r.UserId == UserId);
        if (resume == null) return NotFound();
        _context.Resumes.Remove(resume);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Resume?> LoadResume(int userId)
    {
        return await _context.Resumes
            .Include(r => r.User)
            .Include(r => r.Experiences.OrderBy(e => e.Order))
            .Include(r => r.Educations.OrderBy(e => e.Order))
            .Include(r => r.Skills.OrderBy(s => s.Order))
            .Include(r => r.Languages.OrderBy(l => l.Order))
            .FirstOrDefaultAsync(r => r.UserId == userId);
    }

    private static ResumeDto MapToDto(Resume r) => new()
    {
        Id = r.Id, Title = r.Title, Summary = r.Summary,
        CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt,
        UserFullName = $"{r.User.FirstName} {r.User.LastName}",
        UserEmail = r.User.Email, UserPhone = r.User.Phone,
        Experiences = r.Experiences.Select(e => new ResumeExperienceDto
        {
            Id = e.Id, JobTitle = e.JobTitle, Company = e.Company, Location = e.Location,
            StartDate = e.StartDate, EndDate = e.EndDate, IsCurrent = e.IsCurrent,
            Description = e.Description, Order = e.Order
        }).ToList(),
        Educations = r.Educations.Select(e => new ResumeEducationDto
        {
            Id = e.Id, Degree = e.Degree, School = e.School, Location = e.Location,
            StartDate = e.StartDate, EndDate = e.EndDate, IsCurrent = e.IsCurrent,
            Description = e.Description, Order = e.Order
        }).ToList(),
        Skills = r.Skills.Select(s => new ResumeSkillDto
        {
            Id = s.Id, Name = s.Name, Level = s.Level, Order = s.Order
        }).ToList(),
        Languages = r.Languages.Select(l => new ResumeLanguageDto
        {
            Id = l.Id, Name = l.Name, Level = l.Level, Order = l.Order
        }).ToList()
    };
}
