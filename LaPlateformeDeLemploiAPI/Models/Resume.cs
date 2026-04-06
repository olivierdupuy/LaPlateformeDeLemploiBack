namespace LaPlateformeDeLemploiAPI.Models;

public class Resume
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public ICollection<ResumeExperience> Experiences { get; set; } = new List<ResumeExperience>();
    public ICollection<ResumeEducation> Educations { get; set; } = new List<ResumeEducation>();
    public ICollection<ResumeSkill> Skills { get; set; } = new List<ResumeSkill>();
    public ICollection<ResumeLanguage> Languages { get; set; } = new List<ResumeLanguage>();
}

public class ResumeExperience
{
    public int Id { get; set; }
    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public string? Description { get; set; }
    public int Order { get; set; }

    public int ResumeId { get; set; }
    public Resume Resume { get; set; } = null!;
}

public class ResumeEducation
{
    public int Id { get; set; }
    public string Degree { get; set; } = string.Empty;
    public string School { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public string? Description { get; set; }
    public int Order { get; set; }

    public int ResumeId { get; set; }
    public Resume Resume { get; set; } = null!;
}

public class ResumeSkill
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; } = 3; // 1-5
    public int Order { get; set; }

    public int ResumeId { get; set; }
    public Resume Resume { get; set; } = null!;
}

public class ResumeLanguage
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Level { get; set; } = "Intermediaire"; // Debutant, Intermediaire, Avance, Bilingue, Natif
    public int Order { get; set; }

    public int ResumeId { get; set; }
    public Resume Resume { get; set; } = null!;
}
