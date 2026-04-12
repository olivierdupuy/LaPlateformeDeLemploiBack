using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class Announcement
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Type { get; set; } = "info"; // info, warning, success, danger

    [MaxLength(30)]
    public string? TargetRole { get; set; } // null = all, Candidate, Recruiter

    public bool IsBanner { get; set; } = false; // Show as site-wide banner

    public bool IsActive { get; set; } = true;

    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public string? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
