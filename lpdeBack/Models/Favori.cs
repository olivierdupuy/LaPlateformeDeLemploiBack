using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>
/// Une offre mise de cote par quelqu'un.
///
/// Les favoris vivaient jusqu'ici dans le stockage local de chaque client,
/// sous la meme clef des deux cotes — donc avec le meme comportement, mais
/// sans jamais se rejoindre. Mettre une offre de cote sur le site ne la
/// faisait pas apparaitre sur le telephone, et l'effacement des donnees du
/// navigateur les emportait sans prevenir.
///
/// La table ne porte rien d'autre que le lien : ce qui interesse la
/// personne, c'est l'offre, et l'offre existe deja.
/// </summary>
public class Favori
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    public int JobOfferId { get; set; }
    public JobOffer JobOffer { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
