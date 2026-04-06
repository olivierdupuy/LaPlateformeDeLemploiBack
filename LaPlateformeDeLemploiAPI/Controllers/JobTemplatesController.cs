using System.Security.Claims;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LaPlateformeDeLemploiAPI.Controllers;

[ApiController]
[Route("api/job-templates")]
[Authorize(Roles = "Company")]
public class JobTemplatesController : ControllerBase
{
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public JobTemplatesController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<List<JobTemplate>>> GetAll()
    {
        var user = await _context.Users.FindAsync(UserId);
        var templates = await _context.JobTemplates
            .Where(t => t.CompanyId == user!.CompanyId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        return Ok(templates);
    }

    [HttpPost]
    public async Task<ActionResult<JobTemplate>> Create(JobTemplate dto)
    {
        var user = await _context.Users.FindAsync(UserId);
        dto.CompanyId = user!.CompanyId!.Value;
        dto.Id = 0;
        _context.JobTemplates.Add(dto);
        await _context.SaveChangesAsync();
        return Ok(dto);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _context.Users.FindAsync(UserId);
        var template = await _context.JobTemplates.FindAsync(id);
        if (template == null) return NotFound();
        if (template.CompanyId != user!.CompanyId) return Forbid();
        _context.JobTemplates.Remove(template);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
