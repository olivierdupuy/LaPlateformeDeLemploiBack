namespace LaPlateformeDeLemploiAPI.Models;

public class Favorite
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int JobOfferId { get; set; }
    public JobOffer JobOffer { get; set; } = null!;
}
