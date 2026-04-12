using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class JobNote
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public int JobOfferId { get; set; }
    public JobOffer JobOffer { get; set; } = null!;

    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
