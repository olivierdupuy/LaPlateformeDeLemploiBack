using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.DTOs;

public class JobOfferCreateDto
{
    [Required(ErrorMessage = "Indiquez l'intitulé du poste.")]
    [StringLength(Limites.Ligne, MinimumLength = 3, ErrorMessage = "L'intitulé fait entre 3 et 200 caractères.")]
    [SansBalisage]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indiquez le nom de l'entreprise.")]
    [StringLength(Limites.Ligne, MinimumLength = 2, ErrorMessage = "Le nom de l'entreprise fait entre 2 et 200 caractères.")]
    [SansBalisage]
    public string Company { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indiquez le lieu du poste.")]
    [StringLength(Limites.Ligne, MinimumLength = 2, ErrorMessage = "Le lieu fait entre 2 et 200 caractères.")]
    [SansBalisage]
    public string Location { get; set; } = string.Empty;

    // La description est le corps de l'annonce : c'est le seul champ qui a
    // le droit d'etre long, et le seul ou « < » se rencontre legitimement.
    [Required(ErrorMessage = "Décrivez le poste.")]
    [StringLength(Limites.Texte, MinimumLength = 20, ErrorMessage = "La description fait entre 20 et 20 000 caractères.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indiquez le type de contrat.")]
    [Longueur(Limites.Nom), SansBalisage]
    public string ContractType { get; set; } = string.Empty;

    [Longueur(Limites.Nom), SansBalisage]
    public string? Salary { get; set; }

    [Required(ErrorMessage = "Choisissez une catégorie.")]
    [Longueur(Limites.Ligne), SansBalisage]
    public string Category { get; set; } = string.Empty;

    public bool IsRemote { get; set; }
    public DateTime? ExpiresAt { get; set; }

    [AdresseWeb]
    public string? CompanyLogoUrl { get; set; }

    [Longueur(Limites.Url), SansBalisage]
    public string? Tags { get; set; }

    // Les fourchettes alimentent les filtres et la page « Salaires ». Une
    // borne aberrante ne casse rien visiblement — elle deforme les
    // estimations d'un metier entier, ce qui se remarque bien plus tard.
    [Range(0, 1_000_000, ErrorMessage = "Le salaire minimum doit être compris entre 0 € et 1 000 000 €.")]
    public int? MinSalary { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Le salaire maximum doit être compris entre 0 € et 1 000 000 €.")]
    public int? MaxSalary { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? ExperienceRequired { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? EducationLevel { get; set; }

    [Longueur(Limites.Paragraphe)]
    public string? Benefits { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? WorkSchedule { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Languages { get; set; }

    [Longueur(Limites.Texte)]
    public string? CompanyDescription { get; set; }

    public bool IsUrgent { get; set; }
    public bool EasyApply { get; set; } = true;

    [Longueur(Limites.Texte)]
    public string? ScreeningQuestions { get; set; }

    [Longueur(Limites.Paragraphe)]
    public string? AutoReplyMessage { get; set; }

    // Depot d'offre (tunnel type Indeed)
    [Range(1, 9_999, ErrorMessage = "Le nombre de postes doit être compris entre 1 et 9 999.")]
    public int Openings { get; set; } = 1;

    [Longueur(Limites.Nom), SansBalisage]
    public string? WorkplaceType { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Address { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? SalaryPeriod { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? SupplementalPay { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? ContractDuration { get; set; }

    [Range(1, 80, ErrorMessage = "Le nombre d'heures par semaine doit être compris entre 1 et 80.")]
    public int? HoursPerWeek { get; set; }

    public DateTime? StartDate { get; set; }

    // C'est l'adresse qui recevra les candidatures : une adresse fautive
    // les envoie dans le vide, sans que personne s'en apercoive.
    [AdresseCourriel]
    public string? ApplicationEmail { get; set; }

    public bool RequireResume { get; set; } = true;

    /// <summary>Enregistrer sans publier : l'offre reste invisible des candidats.</summary>
    public bool IsDraft { get; set; }
}

public class JobOfferUpdateDto : JobOfferCreateDto
{
    public bool IsActive { get; set; } = true;
}

public class JobReportDto
{
    [Required(ErrorMessage = "Indiquez le motif du signalement.")]
    [Longueur(Limites.Ligne), SansBalisage]
    public string Reason { get; set; } = string.Empty;

    [Longueur(Limites.Paragraphe)]
    public string? Details { get; set; }

    [AdresseCourriel]
    public string? ReporterEmail { get; set; }
}
