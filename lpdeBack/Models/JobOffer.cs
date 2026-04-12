using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class JobOffer
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Company { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Location { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ContractType { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Salary { get; set; }

    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    public bool IsRemote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? CompanyLogoUrl { get; set; }

    [MaxLength(500)]
    public string? Tags { get; set; }

    // New fields
    public int? MinSalary { get; set; } // Salaire min annuel brut en EUR

    public int? MaxSalary { get; set; } // Salaire max annuel brut en EUR

    [MaxLength(50)]
    public string? ExperienceRequired { get; set; } // Junior, Intermediaire, Senior, Expert

    [MaxLength(100)]
    public string? EducationLevel { get; set; } // Bac, Bac+2, Bac+3, Bac+5, Doctorat

    [MaxLength(1000)]
    public string? Benefits { get; set; } // Avantages (virgules) : Teletravail, Tickets resto, RTT...

    [MaxLength(1000)]
    public string? CompanyDescription { get; set; }

    public bool IsUrgent { get; set; } = false;

    public int ViewCount { get; set; } = 0;

    // FK
    public string? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
