using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>Question posée sur une entreprise (Q&A communautaire).</summary>
public class CompanyQuestion
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Company { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Body { get; set; } = string.Empty;

    public string? AuthorUserId { get; set; }

    [MaxLength(120)]
    public string? AuthorName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CompanyAnswer> Answers { get; set; } = new List<CompanyAnswer>();
}

/// <summary>Réponse à une question d'entreprise.</summary>
public class CompanyAnswer
{
    public int Id { get; set; }

    public int CompanyQuestionId { get; set; }
    public CompanyQuestion? Question { get; set; }

    [Required, MaxLength(1000)]
    public string Body { get; set; } = string.Empty;

    public string? AuthorUserId { get; set; }

    [MaxLength(120)]
    public string? AuthorName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Un utilisateur suit une entreprise (pour ses nouvelles offres).</summary>
public class CompanyFollow
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Company { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
