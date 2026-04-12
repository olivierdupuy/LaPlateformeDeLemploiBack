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
    public string? CompanyDescription { get; set; }
    public bool IsUrgent { get; set; }
}

public class JobOfferUpdateDto : JobOfferCreateDto
{
    public bool IsActive { get; set; } = true;
}
