namespace lpdeBack.DTOs;

public class JobOfferCreateDto
{
    public string Title { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContractType { get; set; } = string.Empty;
    public string? Salary { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool IsRemote { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? CompanyLogoUrl { get; set; }
    public string? Tags { get; set; }
    public int? MinSalary { get; set; }
    public int? MaxSalary { get; set; }
    public string? ExperienceRequired { get; set; }
    public string? EducationLevel { get; set; }
    public string? Benefits { get; set; }
    public string? WorkSchedule { get; set; }
    public string? Languages { get; set; }
    public string? CompanyDescription { get; set; }
    public bool IsUrgent { get; set; }
    public bool EasyApply { get; set; } = true;
    public string? ScreeningQuestions { get; set; }
    public string? AutoReplyMessage { get; set; }

    // Depot d'offre (tunnel type Indeed)
    public int Openings { get; set; } = 1;
    public string? WorkplaceType { get; set; }
    public string? Address { get; set; }
    public string? SalaryPeriod { get; set; }
    public string? SupplementalPay { get; set; }
    public string? ContractDuration { get; set; }
    public int? HoursPerWeek { get; set; }
    public DateTime? StartDate { get; set; }
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
    public string Reason { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? ReporterEmail { get; set; }
}
