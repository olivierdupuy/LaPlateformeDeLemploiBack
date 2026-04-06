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
public class FavoritesController : ControllerBase
{
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public FavoritesController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<List<FavoriteDto>>> GetMyFavorites()
    {
        var favs = await _context.Favorites
            .Where(f => f.UserId == UserId)
            .Include(f => f.JobOffer).ThenInclude(j => j.Company)
            .Include(f => f.JobOffer).ThenInclude(j => j.Category)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FavoriteDto
            {
                Id = f.Id,
                CreatedAt = f.CreatedAt,
                JobOffer = new JobOfferDto
                {
                    Id = f.JobOffer.Id, Title = f.JobOffer.Title,
                    Description = f.JobOffer.Description, Location = f.JobOffer.Location,
                    ContractType = f.JobOffer.ContractType,
                    SalaryMin = f.JobOffer.SalaryMin, SalaryMax = f.JobOffer.SalaryMax,
                    IsRemote = f.JobOffer.IsRemote, PublishedAt = f.JobOffer.PublishedAt,
                    IsActive = f.JobOffer.IsActive,
                    CompanyId = f.JobOffer.CompanyId, CompanyName = f.JobOffer.Company.Name,
                    CompanyLogoUrl = f.JobOffer.Company.LogoUrl,
                    CategoryId = f.JobOffer.CategoryId, CategoryName = f.JobOffer.Category.Name
                }
            })
            .ToListAsync();

        return Ok(favs);
    }

    [HttpPost("{jobOfferId}")]
    public async Task<ActionResult> Toggle(int jobOfferId)
    {
        var existing = await _context.Favorites
            .FirstOrDefaultAsync(f => f.UserId == UserId && f.JobOfferId == jobOfferId);

        if (existing != null)
        {
            _context.Favorites.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok(new { favorited = false });
        }

        _context.Favorites.Add(new Favorite { UserId = UserId, JobOfferId = jobOfferId });
        await _context.SaveChangesAsync();
        return Ok(new { favorited = true });
    }

    [HttpGet("check/{jobOfferId}")]
    public async Task<ActionResult> IsFavorited(int jobOfferId)
    {
        var exists = await _context.Favorites.AnyAsync(f => f.UserId == UserId && f.JobOfferId == jobOfferId);
        return Ok(new { favorited = exists });
    }

    [HttpGet("ids")]
    public async Task<ActionResult<List<int>>> GetFavoriteIds()
    {
        var ids = await _context.Favorites
            .Where(f => f.UserId == UserId)
            .Select(f => f.JobOfferId)
            .ToListAsync();
        return Ok(ids);
    }
}
