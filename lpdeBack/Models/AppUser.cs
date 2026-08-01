using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

public class AppUser : IdentityUser
{
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [MaxLength(50)]
    public string Role { get; set; } = "Candidate";

    [MaxLength(200)]
    public string? Company { get; set; }

    [MaxLength(500)]
    public string? Bio { get; set; }

    [MaxLength(500)]
    public string? ResumeUrl { get; set; }

    // New fields
    [MaxLength(150)]
    public string? Title { get; set; } // Poste actuel ou recherche

    [MaxLength(500)]
    public string? Skills { get; set; } // Competences separees par virgules

    public int? ExperienceYears { get; set; }

    [MaxLength(200)]
    public string? Education { get; set; } // Diplome / Formation

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(300)]
    public string? LinkedInUrl { get; set; }

    [MaxLength(300)]
    public string? PortfolioUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    /// <summary>Le candidat accepte d'apparaitre dans le vivier / d'etre trouve par les recruteurs.</summary>
    public bool IsSearchable { get; set; } = true;

    // ══ Securite ══
    // IdentityUser porte deja TwoFactorEnabled, EmailConfirmed, LockoutEnd,
    // AccessFailedCount et SecurityStamp. Ce qui manque, ce sont les dates :
    // « la double authentification est active » ne dit pas depuis quand, et
    // c'est cette date qu'on cherche quand on enquete sur un compte.

    /// <summary>Depuis quand la double authentification protege ce compte.</summary>
    public DateTime? TwoFactorEnabledAt { get; set; }

    /// <summary>
    /// Par quoi le second facteur se prouve : « Totp » (application) ou
    /// « Sms ». IdentityUser ne porte qu'un booleen — il sait que la double
    /// authentification est active, pas comment la verifier. Sans cette
    /// colonne, la connexion ne saurait pas s'il faut envoyer un SMS ou
    /// attendre un code deja affiche.
    /// </summary>
    [MaxLength(10)]
    public string? TwoFactorMethod { get; set; }

    /// <summary>
    /// Dernier SMS expedie a ce compte. Chaque envoi coute un credit :
    /// sans cette date, un formulaire laisse en boucle viderait le compte
    /// OVH en une nuit.
    /// </summary>
    public DateTime? LastSmsSentAt { get; set; }

    /// <summary>Dernier changement de mot de passe : un mot de passe qui n'a jamais bouge est un fait.</summary>
    public DateTime? LastPasswordChangedAt { get; set; }

    /// <summary>
    /// Derniere connexion reussie. Elle se deduisait du journal d'activite,
    /// au prix d'une requete d'agregation par fiche consultee.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Le preavis de fermeture pour inactivite a-t-il ete envoye ?
    ///
    /// Sans ce drapeau, un compte inactif recevrait le meme avertissement
    /// chaque nuit pendant deux mois. Il se remet a false a la connexion
    /// suivante, qui repousse aussi l'echeance.
    /// </summary>
    public bool PreavisSuppressionEnvoye { get; set; }
}
