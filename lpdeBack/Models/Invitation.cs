using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>
/// Un recruteur invite un profil du vivier a postuler sur une offre.
///
/// Le vivier permettait de trouver quelqu'un et de le regarder. Pour lui
/// parler, il fallait passer par la messagerie, hors de toute offre :
/// le candidat recevait « bonjour, votre profil m'interesse » sans savoir
/// pour quel poste, et rien ne rattachait cet echange a un recrutement.
///
/// L'invitation est une proposition, pas une candidature. Le candidat
/// reste libre de ne pas y donner suite, et son silence n'est pas un
/// refus — c'est pourquoi il n'existe pas d'etat « ignoree » : compter
/// les silences reviendrait a noter les gens sur leur reactivite a des
/// sollicitations qu'ils n'ont pas demandees.
/// </summary>
public class Invitation
{
    public int Id { get; set; }

    public int JobOfferId { get; set; }
    public JobOffer JobOffer { get; set; } = null!;

    /// <summary>Le candidat invite.</summary>
    [MaxLength(450)]
    public string CandidatId { get; set; } = string.Empty;

    /// <summary>Qui a invite. Sert au suivi cote equipe.</summary>
    [MaxLength(450)]
    public string? RecruteurId { get; set; }

    /// <summary>
    /// Un mot du recruteur. Facultatif, et c'est voulu : une invitation
    /// sans phrase reste une invitation, et exiger un message ferait
    /// ecrire des formules creuses.
    /// </summary>
    [MaxLength(1000)]
    public string? Message { get; set; }

    public DateTime EnvoyeeLe { get; set; } = DateTime.UtcNow;

    /// <summary>Quand le candidat l'a ouverte. Nul tant qu'il ne l'a pas vue.</summary>
    public DateTime? VueLe { get; set; }

    /// <summary>
    /// « declinee » si le candidat a dit non, « postule » s'il a depose un
    /// dossier. Nul tant qu'il n'a rien fait — un silence n'est pas une
    /// reponse.
    /// </summary>
    [MaxLength(20)]
    public string? Reponse { get; set; }

    public DateTime? ReponduLe { get; set; }

    public const string Declinee = "declinee";
    public const string Postule = "postule";
}
