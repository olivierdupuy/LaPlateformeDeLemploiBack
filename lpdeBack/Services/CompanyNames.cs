namespace lpdeBack.Services;

/// <summary>
/// Le nom d'employeur que les imports posent quand la source n'en communique
/// aucun. Ce n'est pas une entreprise : c'est l'absence d'entreprise.
///
/// Sans traitement particulier, ce libellé unique regroupe des milliers d'offres
/// sans rapport entre elles et trone en tete du classement des employeurs.
/// </summary>
public static class CompanyNames
{
    public const string Undisclosed = "Entreprise";

    public static bool IsUndisclosed(string? company) =>
        string.Equals(company?.Trim(), Undisclosed, StringComparison.OrdinalIgnoreCase);
}
