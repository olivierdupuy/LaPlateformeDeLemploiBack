namespace LaPlateformeDeLemploiAPI.Models;

public class JobPreferences
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Contrats souhaites (virgule-separes: "CDI,CDD,Freelance")
    public string? DesiredContractTypes { get; set; }

    // Villes souhaitees (virgule-separees: "Paris,Lyon,Remote")
    public string? DesiredLocations { get; set; }

    // Categories souhaitees (ids virgule-separes: "1,3,4")
    public string? DesiredCategoryIds { get; set; }

    // Salaire
    public decimal? MinSalary { get; set; }

    // Remote
    public bool? PreferRemote { get; set; }

    // Mots-cles (virgule-separes: "Angular,.NET,Cloud")
    public string? Keywords { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
