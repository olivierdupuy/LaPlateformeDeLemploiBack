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
    /// <summary>
    /// Le role au sein de l'equipe de recrutement : « proprietaire » ou
    /// « membre ». Sans objet pour un candidat.
    ///
    /// Le partage etait binaire : tout membre d'une meme entreprise
    /// pouvait modifier, suspendre et supprimer les offres de tous les
    /// autres. Cela convient a deux associes, pas a une equipe de dix ou
    /// un cabinet qui recrute pour ses clients — il suffisait d'un
    /// nouvel arrivant pour que le catalogue entier soit a sa main.
    ///
    /// Un membre gere ses propres offres et LIT celles de l'equipe : la
    /// visibilite partagee est ce qui fait l'interet du travail a
    /// plusieurs, et elle ne change pas. C'est l'ecriture qui se
    /// restreint.
    /// </summary>
    [MaxLength(20)]
    public string RoleEquipe { get; set; } = RolesEquipe.Membre;

    public bool IsSearchable { get; set; } = true;

    /// <summary>
    /// A partir de quand le candidat peut prendre un poste. Nul s'il ne
    /// l'a pas dit.
    ///
    /// Une date et non un booleen : « disponible immediatement » se perime
    /// tout seul, et un boolean pose il y a huit mois ment sans que
    /// personne ne s'en apercoive. Une date, elle, reste vraie — « a
    /// partir du 1er septembre » se lit encore correctement en octobre.
    /// </summary>
    public DateTime? DisponibleLe { get; set; }

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
