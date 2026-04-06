using System.Security.Claims;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.DTOs;
using LaPlateformeDeLemploiAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LaPlateformeDeLemploiAPI.Controllers;

[ApiController]
[Route("api/company-reviews")]
public class CompanyReviewsController : ControllerBase
{
    private readonly AppDbContext _context;

    public CompanyReviewsController(AppDbContext context) => _context = context;

    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string UserRole => User.FindFirstValue(ClaimTypes.Role)!;

    // GET: /api/company-reviews/{companyId} - Get all reviews for a company (public)
    [HttpGet("{companyId}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<ReviewResponseDto>>> GetReviews(int companyId)
    {
        var reviews = await _context.CompanyReviews
            .Where(r => r.CompanyId == companyId)
            .Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewResponseDto
            {
                Id = r.Id,
                Rating = r.Rating,
                Comment = r.Comment,
                InterviewPosition = r.InterviewPosition,
                CreatedAt = r.CreatedAt,
                UserFullName = r.User.FirstName + " " + r.User.LastName,
                UserInitials = r.User.FirstName.Substring(0, 1) + r.User.LastName.Substring(0, 1)
            })
            .ToListAsync();

        return Ok(reviews);
    }

    // GET: /api/company-reviews/{companyId}/rating - Get average rating + distribution (public)
    [HttpGet("{companyId}/rating")]
    [AllowAnonymous]
    public async Task<ActionResult<CompanyRatingDto>> GetRating(int companyId)
    {
        var reviews = await _context.CompanyReviews
            .Where(r => r.CompanyId == companyId)
            .ToListAsync();

        if (reviews.Count == 0)
        {
            return Ok(new CompanyRatingDto
            {
                AverageRating = 0,
                TotalReviews = 0,
                Distribution = new Dictionary<int, int>
                {
                    { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 }
                }
            });
        }

        var distribution = new Dictionary<int, int>
        {
            { 1, 0 }, { 2, 0 }, { 3, 0 }, { 4, 0 }, { 5, 0 }
        };
        foreach (var r in reviews)
        {
            distribution[r.Rating]++;
        }

        return Ok(new CompanyRatingDto
        {
            AverageRating = Math.Round(reviews.Average(r => r.Rating), 1),
            TotalReviews = reviews.Count,
            Distribution = distribution
        });
    }

    // POST: /api/company-reviews - Create review (auth required, JobSeeker only, one per company)
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ReviewResponseDto>> CreateReview(CreateReviewDto dto)
    {
        if (UserRole != "JobSeeker")
            return Forbid();

        if (dto.Rating < 1 || dto.Rating > 5)
            return BadRequest(new { message = "La note doit etre entre 1 et 5." });

        var companyExists = await _context.Companies.AnyAsync(c => c.Id == dto.CompanyId);
        if (!companyExists)
            return NotFound(new { message = "Entreprise introuvable." });

        var alreadyReviewed = await _context.CompanyReviews
            .AnyAsync(r => r.UserId == UserId && r.CompanyId == dto.CompanyId);
        if (alreadyReviewed)
            return BadRequest(new { message = "Vous avez deja laisse un avis pour cette entreprise." });

        var review = new CompanyReview
        {
            UserId = UserId,
            CompanyId = dto.CompanyId,
            Rating = dto.Rating,
            Comment = dto.Comment,
            InterviewPosition = dto.InterviewPosition
        };

        _context.CompanyReviews.Add(review);
        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(UserId);

        return Ok(new ReviewResponseDto
        {
            Id = review.Id,
            Rating = review.Rating,
            Comment = review.Comment,
            InterviewPosition = review.InterviewPosition,
            CreatedAt = review.CreatedAt,
            UserFullName = user!.FirstName + " " + user.LastName,
            UserInitials = user.FirstName.Substring(0, 1) + user.LastName.Substring(0, 1)
        });
    }

    // DELETE: /api/company-reviews/{id} - Delete own review (auth required)
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteReview(int id)
    {
        var review = await _context.CompanyReviews.FindAsync(id);
        if (review == null)
            return NotFound();

        if (review.UserId != UserId)
            return Forbid();

        _context.CompanyReviews.Remove(review);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
