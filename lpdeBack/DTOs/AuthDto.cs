using System.ComponentModel.DataAnnotations;

namespace lpdeBack.DTOs;

public class RegisterDto
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Role { get; set; } = "Candidate";

    [MaxLength(200)]
    public string? Company { get; set; }
}

public class LoginDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class UpdateProfileDto
{
    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [MaxLength(200)]
    public string? Company { get; set; }

    [MaxLength(500)]
    public string? Bio { get; set; }

    [MaxLength(150)]
    public string? Title { get; set; }

    [MaxLength(500)]
    public string? Skills { get; set; }

    public int? ExperienceYears { get; set; }

    [MaxLength(200)]
    public string? Education { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(300)]
    public string? LinkedInUrl { get; set; }

    [MaxLength(300)]
    public string? PortfolioUrl { get; set; }

    public bool? IsSearchable { get; set; }
}

public class GoogleSignInDto
{
    public string Credential { get; set; } = string.Empty;
}

public class ChangePasswordDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(8)]
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
    public string Email { get; set; } = string.Empty;
}

public class ReinitialisationDto
{
    public string UserId { get; set; } = string.Empty;
    public string Jeton { get; set; } = string.Empty;
    public string NouveauMotDePasse { get; set; } = string.Empty;
}

public class ConfirmationEmailDto
{
    public string UserId { get; set; } = string.Empty;
    public string Jeton { get; set; } = string.Empty;
}

public class DefiDeuxFacteursDto
{
    /// <summary>Le jeton rendu par la connexion, qui ne vaut que pour cette etape.</summary>
    public string ChallengeToken { get; set; } = string.Empty;

    /// <summary>Le code de l'application, ou l'un des codes de secours.</summary>
    public string Code { get; set; } = string.Empty;
}

public class RenvoiDto
{
    public string ChallengeToken { get; set; } = string.Empty;
}

public class LinkedInDto
{
    /// <summary>Le code d'autorisation rendu par LinkedIn a la redirection.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>L'URL de redirection utilisee, que LinkedIn revalide a l'echange.</summary>
    public string RedirectUri { get; set; } = string.Empty;
}
