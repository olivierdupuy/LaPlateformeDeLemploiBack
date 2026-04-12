using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class Interview
{
    public int Id { get; set; }

    public int ApplicationId { get; set; }
    public Application Application { get; set; } = null!;

    public DateTime ProposedAt { get; set; }

    [MaxLength(300)]
    public string? Location { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "Proposed";

    // New fields
    public int? Duration { get; set; } // Duree en minutes

    [MaxLength(50)]
    public string? Type { get; set; } // Telephonique, Visio, Presentiel

    [MaxLength(100)]
    public string? InterviewerName { get; set; }

    [MaxLength(500)]
    public string? CandidateSlots { get; set; } // JSON: proposed alternative slots by candidate

    [MaxLength(500)]
    public string? CandidateMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
