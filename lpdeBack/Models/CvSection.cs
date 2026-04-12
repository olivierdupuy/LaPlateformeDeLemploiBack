using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class CvSection
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    [Required, MaxLength(50)]
    public string SectionType { get; set; } = string.Empty; // Experience, Formation, Langue, Competence, CentreInteret, Projet

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Organization { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Level { get; set; } // Langue: Natif/Courant/B2... Competence: Expert/Avance/Intermediaire

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
