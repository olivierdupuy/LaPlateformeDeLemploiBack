using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.DTOs;

public class SavedSearchCreateDto
{
    [Longueur(Limites.Ligne), SansBalisage]
    public string? Label { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Query { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Category { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? ContractType { get; set; }

    public bool? IsRemote { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Location { get; set; }
}
