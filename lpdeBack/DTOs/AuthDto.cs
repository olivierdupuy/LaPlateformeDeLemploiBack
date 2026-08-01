using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.DTOs;

public class RegisterDto
{
    [Required(ErrorMessage = "Indiquez votre prénom.")]
    [StringLength(Limites.Nom, MinimumLength = 2, ErrorMessage = "Le prénom fait entre 2 et 100 caractères.")]
    [SansBalisage]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indiquez votre nom.")]
    [StringLength(Limites.Nom, MinimumLength = 2, ErrorMessage = "Le nom fait entre 2 et 100 caractères.")]
    [SansBalisage]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indiquez votre adresse e-mail.")]
    [AdresseCourriel]
    public string Email { get; set; } = string.Empty;

    // La borne haute n'est pas décorative : le mot de passe est haché, et
    // hacher un mégaoctet occupe le serveur le temps qu'il faut. Répété,
    // c'est une façon de le mettre à genoux sans rien exploiter.
    [Required(ErrorMessage = "Choisissez un mot de passe.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Le mot de passe fait entre 8 et 128 caractères.")]
    public string Password { get; set; } = string.Empty;

    [Parmi("Candidate", "Recruiter")]
    public string Role { get; set; } = "Candidate";

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Company { get; set; }
}

public class LoginDto
{
    [Required(ErrorMessage = "Indiquez votre adresse e-mail.")]
    [AdresseCourriel]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indiquez votre mot de passe.")]
    [StringLength(128, ErrorMessage = "Mot de passe trop long.")]
    public string Password { get; set; } = string.Empty;
}

public class UpdateProfileDto
{
    [Longueur(Limites.Nom), SansBalisage]
    public string? FirstName { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? LastName { get; set; }

    [AdresseWeb]
    public string? AvatarUrl { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Company { get; set; }

    [StringLength(Limites.Paragraphe, ErrorMessage = "La présentation ne peut pas dépasser 2 000 caractères.")]
    public string? Bio { get; set; }

    [Longueur(150), SansBalisage]
    public string? Title { get; set; }

    [Longueur(Limites.Url), SansBalisage]
    public string? Skills { get; set; }

    // Une carrière ne dure pas soixante-dix ans, et un nombre négatif
    // fausserait tous les filtres qui trient dessus.
    [Range(0, 70, ErrorMessage = "Le nombre d'années d'expérience doit être compris entre 0 et 70.")]
    public int? ExperienceYears { get; set; }

    [Longueur(Limites.Ligne), SansBalisage]
    public string? Education { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? City { get; set; }

    [AdresseWeb]
    public string? LinkedInUrl { get; set; }

    [AdresseWeb]
    public string? PortfolioUrl { get; set; }

    public bool? IsSearchable { get; set; }
}

public class GoogleSignInDto
{
    // Un jeton Google dépasse rarement 2 000 caractères ; au-delà, ce
    // n'en est pas un, et il est inutile d'aller le soumettre à Google.
    [Required, Longueur(4_000)]
    public string Credential { get; set; } = string.Empty;
}

public class ChangePasswordDto
{
    [Required(ErrorMessage = "Indiquez votre mot de passe actuel.")]
    [Longueur(128)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choisissez un nouveau mot de passe.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Le mot de passe fait entre 8 et 128 caractères.")]
    public string NewPassword { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expiration { get; set; }
    public UserDto User { get; set; } = null!;

    // ── Connexion en deux temps ──
    // Quand la double authentification protege le compte, le mot de passe
    // juste ne donne plus de jeton de session : il donne un jeton de defi,
    // qui ne vaut que le temps de saisir un code. Token reste alors vide —
    // c'est la seule facon pour le client de ne pas croire qu'il est entre.

    /// <summary>Un code a six chiffres est attendu avant d'ouvrir la session.</summary>
    public bool RequiresTwoFactor { get; set; }

    /// <summary>Le jeton de defi, valable cinq minutes, sans aucun autre pouvoir.</summary>
    public string? ChallengeToken { get; set; }

    /// <summary>« Totp » ou « Sms » : de quoi savoir quoi demander, et ou chercher le code.</summary>
    public string? TwoFactorMethod { get; set; }

    /// <summary>Le numero masque, quand le code part par SMS : « +33 6 •• •• •• 78 ».</summary>
    public string? TwoFactorTarget { get; set; }

    /// <summary>Ce qui vient de se passer — envoi reussi, ou raison de son echec.</summary>
    public string? TwoFactorMessage { get; set; }
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string? ResumeUrl { get; set; }
    public string? Title { get; set; }
    public string? Skills { get; set; }
    public int? ExperienceYears { get; set; }
    public string? Education { get; set; }
    public string? City { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? PortfolioUrl { get; set; }
    public bool IsSearchable { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOnline { get; set; }

    // ── Securite ──
    // Portees jusqu'au client parce que l'interface s'en sert : un bandeau
    // rappelle l'adresse non confirmee, et un administrateur sans double
    // authentification est conduit a l'activer avant d'entrer.
    public bool EmailConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
}

// ── Recuperation et confirmation ──

public class MotDePasseOublieDto
{
    [Required(ErrorMessage = "Indiquez votre adresse e-mail.")]
    [AdresseCourriel]
    public string Email { get; set; } = string.Empty;
}

public class ReinitialisationDto
{
    [Required, Longueur(450)]
    public string UserId { get; set; } = string.Empty;

    [Required, Longueur(2_000)]
    public string Jeton { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choisissez un nouveau mot de passe.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Le mot de passe fait entre 8 et 128 caractères.")]
    public string NouveauMotDePasse { get; set; } = string.Empty;
}

public class ConfirmationEmailDto
{
    [Required, Longueur(450)]
    public string UserId { get; set; } = string.Empty;

    [Required, Longueur(2_000)]
    public string Jeton { get; set; } = string.Empty;
}

public class DefiDeuxFacteursDto
{
    /// <summary>Le jeton rendu par la connexion, qui ne vaut que pour cette etape.</summary>
    [Required, Longueur(2_000)]
    public string ChallengeToken { get; set; } = string.Empty;

    /// <summary>
    /// Le code de l'application, ou l'un des codes de secours.
    ///
    /// Six chiffres, ou un code de secours de onze signes avec son tiret.
    /// La borne evite qu'on soumette un dictionnaire dans un seul champ.
    /// </summary>
    [Required(ErrorMessage = "Saisissez le code reçu.")]
    [StringLength(20, MinimumLength = 6, ErrorMessage = "Un code compte six chiffres, ou onze signes pour un code de secours.")]
    public string Code { get; set; } = string.Empty;
}

public class RenvoiDto
{
    [Required, Longueur(2_000)]
    public string ChallengeToken { get; set; } = string.Empty;
}

public class LinkedInDto
{
    /// <summary>Le code d'autorisation rendu par LinkedIn a la redirection.</summary>
    [Required, Longueur(2_000)]
    public string Code { get; set; } = string.Empty;

    /// <summary>L'URL de redirection utilisee, que LinkedIn revalide a l'echange.</summary>
    [Required, AdresseWeb]
    public string RedirectUri { get; set; } = string.Empty;
}
