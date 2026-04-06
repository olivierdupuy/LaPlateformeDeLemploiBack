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
public class CompaniesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;

    public CompaniesController(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CompanyDto>>> GetAll()
    {
        var companies = await _context.Companies
            .Include(c => c.JobOffers)
            .OrderBy(c => c.Name)
            .Select(c => new CompanyDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                LogoUrl = c.LogoUrl,
                Website = c.Website,
                Location = c.Location,
                CreatedAt = c.CreatedAt,
                JobOffersCount = c.JobOffers.Count(j => j.IsActive)
            })
            .ToListAsync();

        return Ok(companies);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CompanyDto>> GetById(int id)
    {
        var company = await _context.Companies
            .Include(c => c.JobOffers)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (company == null) return NotFound();

        return Ok(new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Description = company.Description,
            LogoUrl = company.LogoUrl,
            Website = company.Website,
            Location = company.Location,
            CreatedAt = company.CreatedAt,
            JobOffersCount = company.JobOffers.Count(j => j.IsActive)
        });
    }

    [HttpPost]
    public async Task<ActionResult<CompanyDto>> Create(CompanyCreateDto dto)
    {
        var company = new Company
        {
            Name = dto.Name,
            Description = dto.Description,
            LogoUrl = dto.LogoUrl,
            Website = dto.Website,
            Location = dto.Location
        };

        _context.Companies.Add(company);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = company.Id }, new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Description = company.Description,
            LogoUrl = company.LogoUrl,
            Website = company.Website,
            Location = company.Location,
            CreatedAt = company.CreatedAt,
            JobOffersCount = 0
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CompanyDto>> Update(int id, CompanyCreateDto dto)
    {
        var company = await _context.Companies
            .Include(c => c.JobOffers)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (company == null) return NotFound();

        company.Name = dto.Name;
        company.Description = dto.Description;
        company.LogoUrl = dto.LogoUrl;
        company.Website = dto.Website;
        company.Location = dto.Location;

        await _context.SaveChangesAsync();

        return Ok(new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Description = company.Description,
            LogoUrl = company.LogoUrl,
            Website = company.Website,
            Location = company.Location,
            CreatedAt = company.CreatedAt,
            JobOffersCount = company.JobOffers.Count(j => j.IsActive)
        });
    }

    [Authorize]
    [HttpPost("upload-logo")]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Aucun fichier envoye." });

        if (file.Length > 2 * 1024 * 1024)
            return BadRequest(new { message = "Le fichier ne doit pas depasser 2 Mo." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".jpg" && ext != ".jpeg" && ext != ".png" && ext != ".webp")
            return BadRequest(new { message = "Format accepte : JPG, PNG ou WebP." });

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _context.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.Company == null) return Forbid();

        var uploadsDir = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", "logos");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"company-{user.CompanyId}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        user.Company.LogoUrl = $"/uploads/logos/{fileName}?v={DateTime.UtcNow.Ticks}";
        await _context.SaveChangesAsync();

        return Ok(new { logoUrl = user.Company.LogoUrl });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var company = await _context.Companies.FindAsync(id);
        if (company == null) return NotFound();

        _context.Companies.Remove(company);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
