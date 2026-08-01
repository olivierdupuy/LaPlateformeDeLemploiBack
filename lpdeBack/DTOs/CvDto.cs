using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.DTOs;

public class CvSectionCreateDto
{
    // Le type de section n'est pas enumere : le generateur d'IA en produit
    // de nouveaux au fil des CV, et figer la liste ici obligerait a la
    // tenir a jour dans deux endroits pour rien.
    [Required(ErrorMessage = "Indiquez le type de section.")]
    [Longueur(Limites.Nom), SansBalisage]
    public string SectionType { get; set; } = string.Empty;

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Title { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Organization { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Location { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    [StringLength(Limites.Texte, ErrorMessage = "Cette section ne peut pas dépasser 20 000 caractères.")]
    public string? Description { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Level { get; set; }

    [Range(0, 9_999)]
    public int SortOrder { get; set; }
}

public class CvSectionUpdateDto
{
    [Longueur(Limites.Ligne), SansBalisage]
    public string? Title { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Organization { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Location { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    [StringLength(Limites.Texte, ErrorMessage = "Cette section ne peut pas dépasser 20 000 caractères.")]
    public string? Description { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Level { get; set; }

    [Range(0, 9_999)]
    public int SortOrder { get; set; }
}

public class AiGenerateRequestDto
{
    // Ce texte part vers un modele facture au jeton. Sans borne, un seul
    // appel peut couter le prix de mille, et rien n'empeche de le repeter.
    [StringLength(Limites.Texte, ErrorMessage = "Ce contexte ne peut pas dépasser 20 000 caractères.")]
    public string? AdditionalContext { get; set; }
}

public class AiGenerateResponseDto
{
    public List<CvSectionCreateDto> Sections { get; set; } = new();
}
