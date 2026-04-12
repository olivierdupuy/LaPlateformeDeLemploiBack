namespace lpdeBack.DTOs;

public class SavedSearchCreateDto
{
    public string? Label { get; set; }
    public string? Query { get; set; }
    public string? Category { get; set; }
    public string? ContractType { get; set; }
    public bool? IsRemote { get; set; }
    public string? Location { get; set; }
}
