using System.Security.Claims;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LaPlateformeDeLemploiAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Company")]
public class AIController : ControllerBase
{
    private readonly CompanyAIService _ai;
    private readonly AppDbContext _context;
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public AIController(CompanyAIService ai, AppDbContext context)
    {
        _ai = ai;
        _context = context;
    }

    // POST: Generer une description d'offre
    [HttpPost("generate-job")]
    public async Task<ActionResult<GeneratedJobOffer>> GenerateJob([FromBody] GenerateJobRequest req)
    {
        try
        {
            var user = await _context.Users.Include(u => u.Company).FirstAsync(u => u.Id == UserId);
            var result = await _ai.GenerateJobDescription(
                req.Title, req.Keywords, user.Company?.Name, req.ContractType);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception) { return BadRequest(new { message = "Erreur lors de la generation. Reessayez." }); }
    }

    // POST: Scorer un candidat
    [HttpPost("score-candidate")]
    public async Task<ActionResult<CandidateScore>> ScoreCandidate([FromBody] ScoreCandidateRequest req)
    {
        try
        {
            var application = await _context.Applications
                .Include(a => a.JobOffer)
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == req.ApplicationId);

            if (application == null) return NotFound();

            var user = await _context.Users.FindAsync(UserId);
            if (application.JobOffer.CompanyId != user?.CompanyId) return Forbid();

            var result = await _ai.ScoreCandidate(
                application.JobOffer.Title,
                application.JobOffer.Description,
                $"{application.User.FirstName} {application.User.LastName}",
                application.User.Skills,
                application.User.Bio,
                application.CoverLetter);

            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception) { return BadRequest(new { message = "Erreur lors de l'analyse. Reessayez." }); }
    }

    // POST: Generer un email
    [HttpPost("generate-email")]
    public async Task<ActionResult<GeneratedEmail>> GenerateEmail([FromBody] GenerateEmailRequest req)
    {
        try
        {
            var application = await _context.Applications
                .Include(a => a.JobOffer).ThenInclude(j => j.Company)
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == req.ApplicationId);

            if (application == null) return NotFound();

            var user = await _context.Users.FindAsync(UserId);
            if (application.JobOffer.CompanyId != user?.CompanyId) return Forbid();

            var result = await _ai.GenerateEmail(
                req.Type,
                $"{application.User.FirstName} {application.User.LastName}",
                application.JobOffer.Title,
                application.JobOffer.Company.Name,
                req.AdditionalInfo);

            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception) { return BadRequest(new { message = "Erreur lors de la generation. Reessayez." }); }
    }
}

public class GenerateJobRequest
{
    public string Title { get; set; } = "";
    public string? Keywords { get; set; }
    public string? ContractType { get; set; }
}

public class ScoreCandidateRequest
{
    public int ApplicationId { get; set; }
}

public class GenerateEmailRequest
{
    public int ApplicationId { get; set; }
    public string Type { get; set; } = "interview"; // interview, accepted, rejected, info
    public string? AdditionalInfo { get; set; }
}
