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
public class JobOffersController : ControllerBase
{
    private readonly AppDbContext _context;

    public JobOffersController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<JobOfferDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] string? contractType,
        [FromQuery] string? location,
        [FromQuery] bool? isRemote,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12)
    {
        var query = _context.JobOffers
            .Include(j => j.Company)
            .Include(j => j.Category)
            .Where(j => j.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(j =>
                j.Title.ToLower().Contains(s) ||
                j.Description.ToLower().Contains(s) ||
                j.Company.Name.ToLower().Contains(s));
        }

        if (categoryId.HasValue)
            query = query.Where(j => j.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(contractType))
            query = query.Where(j => j.ContractType == contractType);

        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(j => j.Location.ToLower().Contains(location.ToLower()));

        if (isRemote.HasValue)
            query = query.Where(j => j.IsRemote == isRemote.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(j => j.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => MapToDto(j))
            .ToListAsync();

        return Ok(new PagedResult<JobOfferDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<JobOfferDto>> GetById(int id)
    {
        var job = await _context.JobOffers
            .Include(j => j.Company)
            .Include(j => j.Category)
            .FirstOrDefaultAsync(j => j.Id == id);

        if (job == null) return NotFound();

        job.ViewCount++;
        await _context.SaveChangesAsync();

        return Ok(MapToDto(job));
    }

    [HttpPost]
    public async Task<ActionResult<JobOfferDto>> Create(JobOfferCreateDto dto)
    {
        var job = new JobOffer
        {
            Title = dto.Title,
            Description = dto.Description,
            Location = dto.Location,
            ContractType = dto.ContractType,
            SalaryMin = dto.SalaryMin,
            SalaryMax = dto.SalaryMax,
            IsRemote = dto.IsRemote,
            ExpiresAt = dto.ExpiresAt,
            CompanyId = dto.CompanyId,
            CategoryId = dto.CategoryId
        };

        _context.JobOffers.Add(job);
        await _context.SaveChangesAsync();

        await _context.Entry(job).Reference(j => j.Company).LoadAsync();
        await _context.Entry(job).Reference(j => j.Category).LoadAsync();

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, MapToDto(job));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<JobOfferDto>> Update(int id, JobOfferCreateDto dto)
    {
        var job = await _context.JobOffers
            .Include(j => j.Company)
            .Include(j => j.Category)
            .FirstOrDefaultAsync(j => j.Id == id);

        if (job == null) return NotFound();

        job.Title = dto.Title;
        job.Description = dto.Description;
        job.Location = dto.Location;
        job.ContractType = dto.ContractType;
        job.SalaryMin = dto.SalaryMin;
        job.SalaryMax = dto.SalaryMax;
        job.IsRemote = dto.IsRemote;
        job.ExpiresAt = dto.ExpiresAt;
        job.CompanyId = dto.CompanyId;
        job.CategoryId = dto.CategoryId;

        await _context.SaveChangesAsync();

        await _context.Entry(job).Reference(j => j.Company).LoadAsync();
        await _context.Entry(job).Reference(j => j.Category).LoadAsync();

        return Ok(MapToDto(job));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        _context.JobOffers.Remove(job);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("stats")]
    public async Task<ActionResult> GetStats()
    {
        var totalJobs = await _context.JobOffers.CountAsync(j => j.IsActive);
        var totalCompanies = await _context.Companies.CountAsync();
        var totalCategories = await _context.Categories.CountAsync();
        var remoteJobs = await _context.JobOffers.CountAsync(j => j.IsActive && j.IsRemote);

        return Ok(new { totalJobs, totalCompanies, totalCategories, remoteJobs });
    }

    // GET: Mes offres (Company uniquement)
    [Authorize(Roles = "Company")]
    [HttpGet("mine")]
    public async Task<ActionResult> GetMyOffers()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _context.Users.FindAsync(userId);
        if (user?.CompanyId == null) return Forbid();

        var offers = await _context.JobOffers
            .Include(j => j.Company).Include(j => j.Category)
            .Where(j => j.CompanyId == user.CompanyId)
            .OrderByDescending(j => j.PublishedAt)
            .Select(j => new {
                Job = MapToDto(j),
                ApplicationsCount = _context.Applications.Count(a => a.JobOfferId == j.Id),
                PendingCount = _context.Applications.Count(a => a.JobOfferId == j.Id && a.Status == "Pending")
            })
            .ToListAsync();

        return Ok(offers);
    }

    // PUT: Toggle actif/inactif (Company)
    [Authorize(Roles = "Company")]
    [HttpPut("{id}/toggle-active")]
    public async Task<ActionResult> ToggleActive(int id)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _context.Users.FindAsync(userId);
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();
        if (job.CompanyId != user?.CompanyId) return Forbid();

        job.IsActive = !job.IsActive;
        await _context.SaveChangesAsync();
        return Ok(new { job.IsActive });
    }

    private static JobOfferDto MapToDto(JobOffer j) => new()
    {
        Id = j.Id,
        Title = j.Title,
        Description = j.Description,
        Location = j.Location,
        ContractType = j.ContractType,
        SalaryMin = j.SalaryMin,
        SalaryMax = j.SalaryMax,
        IsRemote = j.IsRemote,
        PublishedAt = j.PublishedAt,
        ExpiresAt = j.ExpiresAt,
        IsActive = j.IsActive,
        CompanyId = j.CompanyId,
        CompanyName = j.Company.Name,
        CompanyLogoUrl = j.Company.LogoUrl,
        CategoryId = j.CategoryId,
        CategoryName = j.Category.Name,
        ViewCount = j.ViewCount
    };
}
