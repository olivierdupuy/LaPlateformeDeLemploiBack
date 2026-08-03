using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>
/// Une offre que le candidat ne veut plus voir.
///
/// Le catalogue ramene cent vingt mille annonces et les memes reviennent
/// a chaque visite. Sans moyen d'en ecarter une, la seule facon de ne
/// plus la croiser etait de changer de recherche — c'est-a-dire de
/// renoncer aussi a tout ce qu'elle ramenait de bon.
///
/// C'est un geste de confort, pas une donnee sensible : ce que le
/// candidat ecarte ne remonte a aucun recruteur. L'offre disparait de ses
/// resultats, et de personne d'autre.
/// </summary>
public class OffreEcartee
{
    public int Id { get; set; }

    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public int JobOfferId { get; set; }
    public JobOffer JobOffer { get; set; } = null!;

    public DateTime CreeLe { get; set; } = DateTime.UtcNow;
}
