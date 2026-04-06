namespace LaPlateformeDeLemploiAPI.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }

    public ICollection<JobOffer> JobOffers { get; set; } = new List<JobOffer>();
}
