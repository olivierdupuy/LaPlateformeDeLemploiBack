using System.ComponentModel.DataAnnotations;

namespace LaPlateformeDeLemploiAPI.Models;

public class CompanyReview
{
    public int Id { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    [MaxLength(100)]
    public string? InterviewPosition { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Relations
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
