namespace lpdeBack.DTOs;

public class CandidatePublicDto
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string? Title { get; set; }
    public string? Skills { get; set; }
    public int? ExperienceYears { get; set; }
    public string? Education { get; set; }
    public string? City { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ApplicationCount { get; set; }
}
