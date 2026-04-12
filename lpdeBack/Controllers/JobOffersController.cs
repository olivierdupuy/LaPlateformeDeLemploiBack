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
public class JobOffersController : ControllerBase
{
    private readonly AppDbContext _context;

    public JobOffersController(AppDbContext context) => _context = context;

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin() => User.IsInRole("Admin");

    // ── Public endpoints (no auth) ──

    [HttpGet]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? contractType,
        [FromQuery] bool? isRemote,
        [FromQuery] string? location)
    {
        // Auto-expire offers past their expiration date
        await _context.JobOffers
            .Where(j => j.IsActive && j.ExpiresAt != null && j.ExpiresAt < DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.IsActive, false));

        var query = _context.JobOffers.Where(j => j.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(j => j.Title.Contains(search) || j.Company.Contains(search) || j.Description.Contains(search));

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(j => j.Category == category);

        if (!string.IsNullOrWhiteSpace(contractType))
            query = query.Where(j => j.ContractType == contractType);

        if (isRemote.HasValue)
            query = query.Where(j => j.IsRemote == isRemote.Value);

        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(j => j.Location.Contains(location));

        return await query.OrderByDescending(j => j.CreatedAt).ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<JobOffer>> GetById(int id)
    {
        var userId = GetUserId();
        var job = await _context.JobOffers.Include(j => j.Applications).FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();

        // Increment view count
        job.ViewCount++;
        await _context.SaveChangesAsync();

        // Only show applications if the logged-in user is the creator of this offer or an admin
        var isOwner = userId != null && job.CreatedByUserId == userId;
        if (!isOwner && !IsAdmin())
        {
            job.Applications = new List<Application>();
        }

        return job;
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<string>>> GetCategories()
    {
        return await _context.JobOffers.Where(j => j.IsActive).Select(j => j.Category).Distinct().OrderBy(c => c).ToListAsync();
    }

    [HttpGet("stats")]
    public async Task<ActionResult<object>> GetStats()
    {
        var totalOffers = await _context.JobOffers.CountAsync(j => j.IsActive);
        var totalApplications = await _context.Applications.CountAsync();
        var totalCompanies = await _context.JobOffers.Where(j => j.IsActive).Select(j => j.Company).Distinct().CountAsync();
        var remoteOffers = await _context.JobOffers.CountAsync(j => j.IsActive && j.IsRemote);

        return new { totalOffers, totalApplications, totalCompanies, remoteOffers };
    }

    [HttpGet("companies")]
    public async Task<ActionResult<IEnumerable<object>>> GetCompanies()
    {
        var companies = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Company)
            .Select(g => new { company = g.Key, jobCount = g.Count(), locations = g.Select(j => j.Location).Distinct().ToList() })
            .OrderByDescending(c => c.jobCount)
            .ToListAsync();
        return Ok(companies);
    }

    [HttpGet("company/{companyName}")]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetByCompany(string companyName)
    {
        return await _context.JobOffers
            .Where(j => j.IsActive && j.Company == companyName)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    // ── Protected endpoints (Recruiter / Admin) ──

    [HttpGet("mine")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetMyOffers()
    {
        var userId = GetUserId();
        var query = IsAdmin()
            ? _context.JobOffers.AsQueryable()
            : _context.JobOffers.Where(j => j.CreatedByUserId == userId);

        return await query
            .Include(j => j.Applications)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    [HttpPatch("{id}/renew")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<JobOffer>> RenewOffer(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();
        if (!IsAdmin() && job.CreatedByUserId != GetUserId()) return Forbid();

        job.IsActive = true;
        job.ExpiresAt = DateTime.UtcNow.AddDays(30);
        await _context.SaveChangesAsync();
        return Ok(job);
    }

    [HttpGet("stats/detailed")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<object>> GetDetailedStats()
    {
        var offersByCategory = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Category)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToListAsync();

        var offersByContract = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.ContractType)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToListAsync();

        var appsByStatus = await _context.Applications
            .GroupBy(a => a.Status)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToListAsync();

        var topCompanies = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Company)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .Take(5)
            .ToListAsync();

        var recentApps = await _context.Applications
            .OrderByDescending(a => a.AppliedAt)
            .Take(30)
            .GroupBy(a => a.AppliedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .ToListAsync();

        return new { offersByCategory, offersByContract, appsByStatus, topCompanies, recentApps };
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<JobOffer>> Create(JobOfferCreateDto dto)
    {
        var job = new JobOffer
        {
            Title = dto.Title,
            Company = dto.Company,
            Location = dto.Location,
            Description = dto.Description,
            ContractType = dto.ContractType,
            Salary = dto.Salary,
            Category = dto.Category,
            IsRemote = dto.IsRemote,
            ExpiresAt = dto.ExpiresAt ?? DateTime.UtcNow.AddDays(30),
            CompanyLogoUrl = dto.CompanyLogoUrl,
            Tags = dto.Tags,
            MinSalary = dto.MinSalary,
            MaxSalary = dto.MaxSalary,
            ExperienceRequired = dto.ExperienceRequired,
            EducationLevel = dto.EducationLevel,
            Benefits = dto.Benefits,
            CompanyDescription = dto.CompanyDescription,
            IsUrgent = dto.IsUrgent,
            CreatedByUserId = GetUserId()
        };

        _context.JobOffers.Add(job);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Update(int id, JobOfferUpdateDto dto)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        job.Title = dto.Title;
        job.Company = dto.Company;
        job.Location = dto.Location;
        job.Description = dto.Description;
        job.ContractType = dto.ContractType;
        job.Salary = dto.Salary;
        job.Category = dto.Category;
        job.IsRemote = dto.IsRemote;
        job.ExpiresAt = dto.ExpiresAt;
        job.CompanyLogoUrl = dto.CompanyLogoUrl;
        job.Tags = dto.Tags;
        job.MinSalary = dto.MinSalary;
        job.MaxSalary = dto.MaxSalary;
        job.ExperienceRequired = dto.ExperienceRequired;
        job.EducationLevel = dto.EducationLevel;
        job.Benefits = dto.Benefits;
        job.CompanyDescription = dto.CompanyDescription;
        job.IsUrgent = dto.IsUrgent;
        job.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        _context.JobOffers.Remove(job);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
