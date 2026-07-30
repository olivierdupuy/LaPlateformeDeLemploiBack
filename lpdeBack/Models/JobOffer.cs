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

    [MaxLength(50)]
    public string? WorkSchedule { get; set; } // Horaires : Temps plein, Temps partiel, Journee, Nuit, Week-end

    [MaxLength(200)]
    public string? Languages { get; set; } // Langues demandees (virgules) : Francais, Anglais, Allemand...

    [MaxLength(1000)]
    public string? CompanyDescription { get; set; }

    public bool IsUrgent { get; set; } = false;

    public bool IsFeatured { get; set; } = false;

    public bool EasyApply { get; set; } = true; // Candidature simplifiee (1 clic sur la plateforme)

    [MaxLength(4000)]
    // Questions de preselection. Format riche : [{ text, type, options, required, idealAnswer }].
    // L'ancien format (tableau JSON de chaines) reste lu tel quel par le front.
    public string? ScreeningQuestions { get; set; }

    [MaxLength(500)]
    public string? AutoReplyMessage { get; set; } // Reponse automatique envoyee au candidat a la reception de sa candidature

    // ── Depot d'offre : precisions du poste ──

    public int Openings { get; set; } = 1; // Nombre de postes a pourvoir

    [MaxLength(30)]
    public string? WorkplaceType { get; set; } // Sur site, Hybride, Teletravail

    [MaxLength(250)]
    public string? Address { get; set; } // Adresse precise du lieu de travail

    [MaxLength(20)]
    public string? SalaryPeriod { get; set; } // heure, mois, an (periodicite de MinSalary/MaxSalary)

    [MaxLength(300)]
    public string? SupplementalPay { get; set; } // Primes (virgules) : 13e mois, primes sur objectifs...

    [MaxLength(100)]
    public string? ContractDuration { get; set; } // Duree du CDD / stage / alternance

    public int? HoursPerWeek { get; set; } // Nombre d'heures par semaine

    public DateTime? StartDate { get; set; } // Date de prise de poste souhaitee

    // ── Depot d'offre : reception des candidatures ──

    [MaxLength(150)]
    public string? ApplicationEmail { get; set; } // Email de notification des nouvelles candidatures

    public bool RequireResume { get; set; } = true; // Le CV est-il obligatoire pour postuler

    /// <summary>Offre enregistree en brouillon : jamais visible des candidats,
    /// reprise possible depuis « Mes offres ».</summary>
    public bool IsDraft { get; set; } = false;

    // Geolocalisation (pour la recherche par rayon)
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    [MaxLength(30)]
    public string ModerationStatus { get; set; } = "Approved"; // Pending, Approved, Rejected

    [MaxLength(500)]
    public string? ModerationNote { get; set; }

    public int ViewCount { get; set; } = 0;

    [MaxLength(250)]
    public string? ExternalId { get; set; } // Cle de dedup pour les offres importees (ex: "arbeitnow:slug")

    [MaxLength(50)]
    public string? ExternalSource { get; set; } // arbeitnow, remotive, francetravail

    [MaxLength(500)]
    public string? ExternalUrl { get; set; } // URL de l'offre sur le site source (pour postuler)

    // FK
    public string? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
