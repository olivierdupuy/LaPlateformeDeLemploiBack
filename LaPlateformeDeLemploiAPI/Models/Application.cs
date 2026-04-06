namespace LaPlateformeDeLemploiAPI.Models;

public class Application
{
    public int Id { get; set; }
    public string? CoverLetter { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Reviewed, Accepted, Rejected
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string? InternalNotes { get; set; }

    public int JobOfferId { get; set; }
    public JobOffer JobOffer { get; set; } = null!;
}
