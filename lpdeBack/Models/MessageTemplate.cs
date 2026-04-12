using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class MessageTemplate
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Category { get; set; } = "General"; // General, Acceptation, Refus, Entretien, Info

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
