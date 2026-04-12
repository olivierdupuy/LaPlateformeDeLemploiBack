using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.DTOs;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CandidatesController : ControllerBase
{
    private readonly AppDbContext _context;
    public CandidatesController(AppDbContext context) => _context = context;

    [HttpGet]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<IEnumerable<CandidatePublicDto>>> GetAll([FromQuery] string? search)
    {
        var query = _context.Users.Where(u => u.Role == "Candidate" && u.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.FirstName.Contains(search) || u.LastName.Contains(search) || (u.Bio != null && u.Bio.Contains(search)));

        var candidates = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();

        var result = new List<CandidatePublicDto>();
        foreach (var c in candidates)
        {
            var appCount = await _context.Applications.CountAsync(a => a.UserId == c.Id);
            result.Add(new CandidatePublicDto
            {
                Id = c.Id,
                FirstName = c.FirstName,
                LastName = c.LastName,
                AvatarUrl = c.AvatarUrl,
                Bio = c.Bio,
                Title = c.Title,
                Skills = c.Skills,
                ExperienceYears = c.ExperienceYears,
                Education = c.Education,
                City = c.City,
                CreatedAt = c.CreatedAt,
                ApplicationCount = appCount
            });
        }
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CandidatePublicDto>> GetById(string id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == "Candidate");
        if (user == null) return NotFound();

        var appCount = await _context.Applications.CountAsync(a => a.UserId == id);

        return new CandidatePublicDto
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            AvatarUrl = user.AvatarUrl,
            Bio = user.Bio,
            Title = user.Title,
            Skills = user.Skills,
            ExperienceYears = user.ExperienceYears,
            Education = user.Education,
            City = user.City,
            CreatedAt = user.CreatedAt,
            ApplicationCount = appCount
        };
    }
}
