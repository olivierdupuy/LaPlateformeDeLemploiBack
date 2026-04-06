namespace LaPlateformeDeLemploiAPI.DTOs;

public class ResumeDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string? UserPhone { get; set; }
    public List<ResumeExperienceDto> Experiences { get; set; } = new();
    public List<ResumeEducationDto> Educations { get; set; } = new();
    public List<ResumeSkillDto> Skills { get; set; } = new();
    public List<ResumeLanguageDto> Languages { get; set; } = new();
}

public class ResumeSaveDto
{
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public List<ResumeExperienceDto> Experiences { get; set; } = new();
    public List<ResumeEducationDto> Educations { get; set; } = new();
    public List<ResumeSkillDto> Skills { get; set; } = new();
    public List<ResumeLanguageDto> Languages { get; set; } = new();
}

public class ResumeExperienceDto
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
}

public class ResumeEducationDto
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
}

public class ResumeSkillDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; } = 3;
    public int Order { get; set; }
}

public class ResumeLanguageDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Level { get; set; } = "Intermediaire";
    public int Order { get; set; }
}
