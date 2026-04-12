using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class AppUser : IdentityUser
{
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [MaxLength(50)]
    public string Role { get; set; } = "Candidate";

    [MaxLength(200)]
    public string? Company { get; set; }

    [MaxLength(500)]
    public string? Bio { get; set; }

    [MaxLength(500)]
    public string? ResumeUrl { get; set; }

    // New fields
    [MaxLength(150)]
    public string? Title { get; set; } // Poste actuel ou recherche

    [MaxLength(500)]
    public string? Skills { get; set; } // Competences separees par virgules

    public int? ExperienceYears { get; set; }

    [MaxLength(200)]
    public string? Education { get; set; } // Diplome / Formation

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(300)]
    public string? LinkedInUrl { get; set; }

    [MaxLength(300)]
    public string? PortfolioUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
