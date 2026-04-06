namespace LaPlateformeDeLemploiAPI.DTOs;

public class ApplicationDto
{
    public int Id { get; set; }
    public string? CoverLetter { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int UserId { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string? UserPhone { get; set; }
    public string? UserSkills { get; set; }
    public string? UserBio { get; set; }
    public int JobOfferId { get; set; }
    public string JobOfferTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ContractType { get; set; } = string.Empty;
    public string? InternalNotes { get; set; }
}

public class BulkStatusDto
{
    public List<int> Ids { get; set; } = new();
    public string Status { get; set; } = string.Empty;
}

public class UpdateNotesDto
{
    public string? Notes { get; set; }
}

public class ApplicationCreateDto
{
    public int JobOfferId { get; set; }
    public string? CoverLetter { get; set; }
}

public class SpontaneousApplicationDto
{
    public int Id { get; set; }
    public string? CoverLetter { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; }
    public int UserId { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string? UserPhone { get; set; }
    public string? UserSkills { get; set; }
    public string? UserBio { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
}

public class SpontaneousApplicationCreateDto
{
    public int CompanyId { get; set; }
    public string? CoverLetter { get; set; }
}

public class ApplicationUpdateStatusDto
{
    public string Status { get; set; } = string.Empty; // Reviewed, Accepted, Rejected
}

public class FavoriteDto
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public JobOfferDto JobOffer { get; set; } = null!;
}

public class UpdateProfileDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Bio { get; set; }
    public string? Skills { get; set; }
}

public class AdvancedStatsDto
{
    // Applications par jour (30 derniers jours)
    public List<DailyCountDto> ApplicationsPerDay { get; set; } = new();
    // Applications par statut
    public Dictionary<string, int> ApplicationsByStatus { get; set; } = new();
    // Applications par type de contrat
    public Dictionary<string, int> ApplicationsByContract { get; set; } = new();
    // Top offres par nb candidatures
    public List<TopOfferDto> TopOffers { get; set; } = new();
    // Applications par localisation
    public Dictionary<string, int> ApplicationsByLocation { get; set; } = new();
}

public class DailyCountDto
{
    public string Date { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class TopOfferDto
{
    public string Title { get; set; } = string.Empty;
    public int ApplicationCount { get; set; }
}

public class DashboardStatsDto
{
    // JobSeeker
    public int TotalApplications { get; set; }
    public int PendingApplications { get; set; }
    public int AcceptedApplications { get; set; }
    public int RejectedApplications { get; set; }
    public int TotalFavorites { get; set; }

    // Company
    public int TotalJobOffers { get; set; }
    public int ActiveJobOffers { get; set; }
    public int TotalReceivedApplications { get; set; }
    public int PendingReceivedApplications { get; set; }
}
