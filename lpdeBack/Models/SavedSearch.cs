using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class SavedSearch
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    [MaxLength(200)]
    public string? Label { get; set; }

    [MaxLength(200)]
    public string? Query { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(50)]
    public string? ContractType { get; set; }

    public bool? IsRemote { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; }

    public bool AlertEnabled { get; set; } = false;

    public DateTime? LastAlertAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
