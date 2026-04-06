namespace LaPlateformeDeLemploiAPI.Models;

public class SpontaneousApplication
{
    public int Id { get; set; }
    public string? CoverLetter { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
}
