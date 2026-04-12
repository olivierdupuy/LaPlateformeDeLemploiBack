using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class PlatformSetting
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Type { get; set; } = "string"; // string, bool, int

    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
