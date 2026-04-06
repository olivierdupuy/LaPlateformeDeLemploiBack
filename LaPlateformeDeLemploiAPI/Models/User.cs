namespace LaPlateformeDeLemploiAPI.Models;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = "JobSeeker"; // "JobSeeker" or "Company"
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPasswordChange { get; set; }
    public string? ResetToken { get; set; }
    public DateTime? ResetTokenExpiry { get; set; }

    // Si role = Company, lien vers l'entreprise
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    // Si role = JobSeeker, infos supplementaires
    public string? ResumeUrl { get; set; }
    public string? Skills { get; set; }
    public string? Bio { get; set; }
}
