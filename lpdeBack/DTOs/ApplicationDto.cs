namespace lpdeBack.DTOs;

public class ApplicationCreateDto
{
    public int JobOfferId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? CoverLetter { get; set; }
    public string? ResumeUrl { get; set; }
}

public class ApplicationUpdateStatusDto
{
    public string Status { get; set; } = string.Empty;
}

public class ApplicationUpdateNotesDto
{
    public string? Notes { get; set; }
}
