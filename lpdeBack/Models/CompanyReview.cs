using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>Avis d'un utilisateur sur une entreprise (note globale + 5 critères).</summary>
public class CompanyReview
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Company { get; set; } = string.Empty;

    // Notes sur 5
    public int OverallRating { get; set; }
    public int WorkLifeBalance { get; set; }
    public int PayBenefits { get; set; }
    public int JobSecurity { get; set; }
    public int Management { get; set; }
    public int Culture { get; set; }

    [MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Body { get; set; }

    [MaxLength(100)]
    public string? JobTitle { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; }

    public string? AuthorUserId { get; set; }

    [MaxLength(120)]
    public string? AuthorName { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Approved"; // Approved, Pending, Rejected

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
