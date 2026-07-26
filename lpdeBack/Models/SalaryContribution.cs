using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>Salaire partagé (anonymement) par un utilisateur pour alimenter les estimations.</summary>
public class SalaryContribution
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string JobTitle { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Company { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; }

    public int AmountAnnual { get; set; } // Salaire annuel brut en EUR

    [MaxLength(50)]
    public string? ContractType { get; set; }

    [MaxLength(50)]
    public string? ExperienceLevel { get; set; }

    public string? AuthorUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
