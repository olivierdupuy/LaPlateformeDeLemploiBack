namespace LaPlateformeDeLemploiAPI.DTOs;

public class CreateReviewDto
{
    public int CompanyId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? InterviewPosition { get; set; }
}

public class ReviewResponseDto
{
    public int Id { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? InterviewPosition { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UserFullName { get; set; } = "";
    public string UserInitials { get; set; } = "";
}

public class CompanyRatingDto
{
    public double AverageRating { get; set; }
    public int TotalReviews { get; set; }
    public Dictionary<int, int> Distribution { get; set; } = new();
}
