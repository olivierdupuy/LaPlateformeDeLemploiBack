using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>Fiche « À propos » d'une entreprise (éditée par un recruteur de l'entreprise / admin).</summary>
public class CompanyProfile
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Company { get; set; } = string.Empty;

    public int? FoundedYear { get; set; }

    [MaxLength(50)]
    public string? Size { get; set; } // ex: "11 à 50"

    [MaxLength(100)]
    public string? Industry { get; set; }

    [MaxLength(150)]
    public string? Headquarters { get; set; }

    [MaxLength(300)]
    public string? Website { get; set; }

    [MaxLength(2000)]
    public string? About { get; set; }

    public string? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
