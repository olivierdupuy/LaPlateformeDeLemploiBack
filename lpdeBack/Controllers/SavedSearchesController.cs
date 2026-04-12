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
[Authorize]
public class SavedSearchesController : ControllerBase
{
    private readonly AppDbContext _context;
    public SavedSearchesController(AppDbContext context) => _context = context;
    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll()
    {
        var userId = GetUserId();
        var searches = await _context.SavedSearches
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var results = new List<object>();
        foreach (var s in searches)
        {
            var query = _context.JobOffers.Where(j => j.IsActive).AsQueryable();
            if (!string.IsNullOrEmpty(s.Query)) query = query.Where(j => j.Title.Contains(s.Query) || j.Company.Contains(s.Query));
            if (!string.IsNullOrEmpty(s.Category)) query = query.Where(j => j.Category == s.Category);
            if (!string.IsNullOrEmpty(s.ContractType)) query = query.Where(j => j.ContractType == s.ContractType);
            if (s.IsRemote.HasValue) query = query.Where(j => j.IsRemote == s.IsRemote.Value);
            if (!string.IsNullOrEmpty(s.Location)) query = query.Where(j => j.Location.Contains(s.Location));
            var count = await query.CountAsync();

            results.Add(new { s.Id, s.Label, s.Query, s.Category, s.ContractType, s.IsRemote, s.Location, s.CreatedAt, resultCount = count });
        }
        return Ok(results);
    }

    [HttpPost]
    public async Task<ActionResult> Create(SavedSearchCreateDto dto)
    {
        var userId = GetUserId();
        var count = await _context.SavedSearches.CountAsync(s => s.UserId == userId);
        if (count >= 20) return BadRequest("Maximum 20 recherches sauvegardees.");

        var search = new SavedSearch
        {
            UserId = userId,
            Label = dto.Label ?? BuildLabel(dto),
            Query = dto.Query,
            Category = dto.Category,
            ContractType = dto.ContractType,
            IsRemote = dto.IsRemote,
            Location = dto.Location
        };

        _context.SavedSearches.Add(search);
        await _context.SaveChangesAsync();
        return Ok(search);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var search = await _context.SavedSearches.FindAsync(id);
        if (search == null || search.UserId != GetUserId()) return NotFound();
        _context.SavedSearches.Remove(search);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static string BuildLabel(SavedSearchCreateDto dto)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(dto.Query)) parts.Add($"\"{dto.Query}\"");
        if (!string.IsNullOrEmpty(dto.Category)) parts.Add(dto.Category);
        if (!string.IsNullOrEmpty(dto.ContractType)) parts.Add(dto.ContractType);
        if (dto.IsRemote == true) parts.Add("Teletravail");
        if (!string.IsNullOrEmpty(dto.Location)) parts.Add(dto.Location);
        return parts.Count > 0 ? string.Join(" · ", parts) : "Recherche sauvegardee";
    }
}
