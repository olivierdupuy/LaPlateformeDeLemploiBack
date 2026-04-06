namespace LaPlateformeDeLemploiAPI.DTOs;

public class PreferencesDto
{
    public List<string> DesiredContractTypes { get; set; } = new();
    public List<string> DesiredLocations { get; set; } = new();
    public List<int> DesiredCategoryIds { get; set; } = new();
    public decimal? MinSalary { get; set; }
    public bool? PreferRemote { get; set; }
    public List<string> Keywords { get; set; } = new();
}

public class RecommendedJobDto
{
    public JobOfferDto Job { get; set; } = null!;
    public int MatchScore { get; set; }
    public List<string> MatchReasons { get; set; } = new();
}
