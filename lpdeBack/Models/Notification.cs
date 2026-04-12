using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class Notification
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Link { get; set; }

    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // NouveauCandidat, StatutModifie, OffreExpiree

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
