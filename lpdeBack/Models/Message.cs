using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class Message
{
    public int Id { get; set; }

    [Required]
    public string SenderId { get; set; } = string.Empty;
    public AppUser Sender { get; set; } = null!;

    [Required]
    public string ReceiverId { get; set; } = string.Empty;
    public AppUser Receiver { get; set; } = null!;

    public int ApplicationId { get; set; }
    public Application Application { get; set; } = null!;

    [Required, MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
