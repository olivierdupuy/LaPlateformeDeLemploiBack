using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>Événement emploi (salon, webinaire, job dating…).</summary>
public class JobEvent
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Type { get; set; } = "Salon"; // Salon, Webinaire, Job dating, Conference

    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public bool IsOnline { get; set; }

    [MaxLength(150)]
    public string? Location { get; set; }

    [MaxLength(300)]
    public string? Url { get; set; }

    [MaxLength(100)]
    public string? Organizer { get; set; }

    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
