namespace lpdeBack.DTOs;

public class InterviewCreateDto
{
    public int ApplicationId { get; set; }
    public DateTime ProposedAt { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public int? Duration { get; set; }
    public string? Type { get; set; }
    public string? InterviewerName { get; set; }
}

public class InterviewUpdateStatusDto
{
    public string Status { get; set; } = string.Empty;
}
