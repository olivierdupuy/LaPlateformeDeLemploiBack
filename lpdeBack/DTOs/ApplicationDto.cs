using System.ComponentModel.DataAnnotations;
using lpdeBack.Models;
using lpdeBack.Validation;

namespace lpdeBack.DTOs;

public class ApplicationCreateDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Offre inconnue.")]
    public int JobOfferId { get; set; }

    [Required(ErrorMessage = "Indiquez votre nom.")]
    [StringLength(Limites.Ligne, MinimumLength = 2, ErrorMessage = "Le nom fait entre 2 et 200 caractères.")]
    [SansBalisage]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indiquez votre adresse e-mail.")]
    [AdresseCourriel]
    public string Email { get; set; } = string.Empty;

    [TelephoneFr]
    public string? Phone { get; set; }

    // La lettre est le seul champ vraiment long du formulaire. Vingt
    // mille signes font huit pages : au-dela, ce n'est plus une lettre,
    // et le recruteur ne la lira pas davantage.
    [StringLength(Limites.Texte, ErrorMessage = "La lettre de motivation ne peut pas dépasser 20 000 caractères.")]
    public string? CoverLetter { get; set; }

    [AdresseWeb]
    public string? ResumeUrl { get; set; }

    // Libre, et volontairement : le client y ecrit « Candidature simplifiee »
    // ou « Candidature complete » selon le tunnel emprunte. Enumerer ces
    // libelles ici les figerait dans deux depots a la fois.
    [Longueur(Limites.Nom), SansBalisage]
    public string? Source { get; set; }

    [Longueur(Limites.Texte)]
    public string? ScreeningAnswers { get; set; }

    // Tunnel de candidature (type Indeed)
    [Longueur(Limites.Nom), SansBalisage]
    public string? City { get; set; }

    public DateTime? AvailableFrom { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? SalaryExpectation { get; set; }
}

public class ApplicationUpdateStatusDto
{
    /// <summary>
    /// La meme liste que celle qu'applique le controleur.
    ///
    /// Elle est repetee ici pour que le refus arrive avec une phrase
    /// utilisable — le controleur rendait « Statut invalide. » en texte
    /// brut, que le client n'affichait pas. Les deux contröles coexistent :
    /// celui-ci parle a l'interesse, l'autre garde la porte si un jour on
    /// appelle la methode autrement.
    /// </summary>
    [Required(ErrorMessage = "Indiquez le statut.")]
    [StatutCandidature]
    public string Status { get; set; } = string.Empty;
}

public class ApplicationArchiveDto
{
    public bool IsArchived { get; set; }
}

public class ApplicationUpdateNotesDto
{
    [StringLength(Limites.Texte, ErrorMessage = "Ces notes ne peuvent pas dépasser 20 000 caractères.")]
    public string? Notes { get; set; }
}
