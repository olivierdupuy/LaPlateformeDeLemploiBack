using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class ActivityLog
{
    public int Id { get; set; }

    public string? UserId { get; set; }
    public AppUser? User { get; set; }

    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Action { get; set; } = string.Empty; // Login, CreateOffer, DeleteOffer, UpdateStatus, ChangeRole, etc.

    [MaxLength(50)]
    public string EntityType { get; set; } = string.Empty; // User, JobOffer, Application, Interview, etc.

    public int? EntityId { get; set; }

    [MaxLength(500)]
    public string Details { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
