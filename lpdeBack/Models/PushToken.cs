using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class PushToken
{
    public int Id { get; set; }
    [Required]
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;
    [Required]
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
