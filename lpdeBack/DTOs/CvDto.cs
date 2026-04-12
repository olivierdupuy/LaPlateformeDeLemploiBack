namespace lpdeBack.DTOs;

public class CvSectionCreateDto
{
    public string SectionType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Organization { get; set; }
    public string? Location { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
    public string? Level { get; set; }
    public int SortOrder { get; set; }
}

public class CvSectionUpdateDto
{
    public string? Title { get; set; }
    public string? Organization { get; set; }
    public string? Location { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
    public string? Level { get; set; }
    public int SortOrder { get; set; }
}

public class AiGenerateRequestDto
{
    public string? AdditionalContext { get; set; }
}

public class AiGenerateResponseDto
{
    public List<CvSectionCreateDto> Sections { get; set; } = new();
}
