using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.DTOs;

public class InterviewCreateDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Candidature inconnue.")]
    public int ApplicationId { get; set; }

    [Required(ErrorMessage = "Indiquez la date et l'heure proposées.")]
    public DateTime ProposedAt { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Location { get; set; }

    [Longueur(Limites.Paragraphe)]
    public string? Notes { get; set; }

    // Un entretien ne dure pas huit heures, et surtout pas zero minute :
    // cette valeur sert a bloquer un creneau dans un agenda.
    [Range(5, 480, ErrorMessage = "La durée doit être comprise entre 5 et 480 minutes.")]
    public int? Duration { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Type { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? InterviewerName { get; set; }
}

public class InterviewUpdateStatusDto
{
    [Required(ErrorMessage = "Indiquez le statut.")]
    [Parmi("Proposed", "Accepted", "Declined", "Cancelled", "Completed")]
    public string Status { get; set; } = string.Empty;
}
