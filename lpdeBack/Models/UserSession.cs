namespace lpdeBack.Models;

/// <summary>
/// Une session ouverte : un jeton emis, et de quoi le reconnaitre et le
/// couper.
///
/// Le jeton vivait sept jours et rien ne pouvait l'arreter. Suspendre un
/// compte le laissait donc en circulation une semaine entiere, et
/// personne — ni l'interesse, ni l'administration — ne pouvait savoir
/// combien d'appareils s'en servaient. Chaque jeton emis laisse desormais
/// une trace ici, et couper cette trace coupe le jeton.
/// </summary>
public class UserSession
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser? User { get; set; }

    /// <summary>L'identifiant unique du jeton, porte par la revendication « jti ».</summary>
    public string Jti { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Derniere requete vue avec ce jeton. Ecrite au plus une fois par
    /// tranche de cinq minutes : une ecriture par requete couterait plus
    /// cher que tout le reste de l'authentification.
    /// </summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>L'agent brut, garde tel quel : c'est la seule preuve.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Ce qu'on en lit : « Chrome sur Windows ». Pour l'affichage seul.</summary>
    public string? Device { get; set; }

    /// <summary>Par quel moyen la session s'est ouverte : Password, Google, LinkedIn, Recovery, Impersonation.</summary>
    public string Method { get; set; } = "Password";

    public DateTime? RevokedAt { get; set; }

    /// <summary>Pourquoi elle a ete coupee : l'interesse doit pouvoir le lire.</summary>
    public string? RevokedReason { get; set; }

    public bool EstActive => RevokedAt == null && ExpiresAt > DateTime.UtcNow;
}
