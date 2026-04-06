namespace LaPlateformeDeLemploiAPI.DTOs;

public class InterviewDto
{
    public int Id { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ApplicationId { get; set; }
    public string JobOfferTitle { get; set; } = string.Empty;
    public string CandidateName { get; set; } = string.Empty;
    public string CandidateEmail { get; set; } = string.Empty;
    public string CompanyUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateInterviewDto
{
    public int ApplicationId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string Type { get; set; } = "Visio";
    public string? Location { get; set; }
    public string? Notes { get; set; }
}

public class UpdateInterviewDto
{
    public DateTime? ScheduledAt { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Type { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public string? Status { get; set; }
}
