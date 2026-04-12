using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class Application
{
    public int Id { get; set; }

    public int JobOfferId { get; set; }

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(150), EmailAddress]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    public string? CoverLetter { get; set; }

    [MaxLength(500)]
    public string? ResumeUrl { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "Pending";

    [MaxLength(2000)]
    public string? RecruiterNotes { get; set; }

    // New fields
    public DateTime? AvailableFrom { get; set; } // Date de disponibilite

    [MaxLength(100)]
    public string? SalaryExpectation { get; set; } // Pretentions salariales

    [MaxLength(100)]
    public string? Source { get; set; } // Plateforme, LinkedIn, Recommandation, Autre

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    public string? UserId { get; set; }
    public AppUser? User { get; set; }

    public JobOffer JobOffer { get; set; } = null!;

    public ICollection<Interview> Interviews { get; set; } = new List<Interview>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
