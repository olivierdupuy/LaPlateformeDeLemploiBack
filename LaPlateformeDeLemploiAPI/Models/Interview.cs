namespace LaPlateformeDeLemploiAPI.Models;

public class Interview
{
    public int Id { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string Type { get; set; } = "Visio"; // Visio, Presentiel, Telephone
    public string? Location { get; set; } // Adresse ou lien visio
    public string? Notes { get; set; }
    public string Status { get; set; } = "Planned"; // Planned, Confirmed, Completed, Cancelled

    public int ApplicationId { get; set; }
    public Application Application { get; set; } = null!;

    public int CompanyUserId { get; set; }
    public User CompanyUser { get; set; } = null!;

    public int CandidateUserId { get; set; }
    public User CandidateUser { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
