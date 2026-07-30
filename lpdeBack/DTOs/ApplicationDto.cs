namespace lpdeBack.DTOs;

public class ApplicationCreateDto
{
    public int JobOfferId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? CoverLetter { get; set; }
    public string? ResumeUrl { get; set; }
    public string? Source { get; set; }
    public string? ScreeningAnswers { get; set; }

    // Tunnel de candidature (type Indeed)
    public string? City { get; set; }
    public DateTime? AvailableFrom { get; set; }
    public string? SalaryExpectation { get; set; }
}

public class ApplicationUpdateStatusDto
{
    public string Status { get; set; } = string.Empty;
}

public class ApplicationArchiveDto
{
    public bool IsArchived { get; set; }
}

public class ApplicationUpdateNotesDto
{
    public string? Notes { get; set; }
}
