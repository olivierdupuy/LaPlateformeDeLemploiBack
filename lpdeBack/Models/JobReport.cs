using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>Signalement d'une offre d'emploi par un utilisateur (contenu abusif, frauduleux...).</summary>
public class JobReport
{
    public int Id { get; set; }

    public int JobOfferId { get; set; }
    public JobOffer? JobOffer { get; set; }

    [Required, MaxLength(60)]
    public string Reason { get; set; } = string.Empty; // Frauduleux, Discriminatoire, Expiree, Doublon, Autre

    [MaxLength(1000)]
    public string? Details { get; set; }

    [MaxLength(256)]
    public string? ReporterEmail { get; set; }

    public string? ReporterUserId { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Pending"; // Pending, Reviewed, Dismissed

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
