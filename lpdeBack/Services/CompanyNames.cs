namespace lpdeBack.Services;

/// <summary>
/// Libellés d'employeur qui ne désignent pas une organisation identifiable.
///
/// Deux familles, traitées pareil au regroupement mais pas à l'affichage :
/// ceux qui ne nomment personne (repli des imports quand la source ne
/// communique pas l'employeur), et les libellés institutionnels que des
/// milliers d'organismes distincts se partagent — « MAIRIE » couvre 793
/// communes sans rapport entre elles.
///
/// Dans les deux cas, en faire une fiche entreprise n'a aucun sens : elle
/// empilerait des offres n'ayant que leur libellé en commun.
///
/// La comparaison porte sur le libellé exact, jamais sur un préfixe :
/// « MAIRIE D'ANNECY » est un employeur reel, avec ses huit offres sur un
/// seul site, et doit rester intact.
/// </summary>
public static class CompanyNames
{
    public const string Undisclosed = "Entreprise";

    /// <summary>Ne nomment aucun employeur : l'offre est anonyme.</summary>
    private static readonly string[] Unnamed = { "ENTREPRISE", "CONFIDENTIEL", "RECRUTEUR", "EMPLOYEUR" };

    /// <summary>Nomment une catégorie d'organisme, pas un organisme.</summary>
    private static readonly string[] Institutional = { "MAIRIE", "COMMUNE", "CCAS", "EHPAD" };

    /// <summary>
    /// Tous ceux qui ne doivent pas former de fiche. En majuscules : les requêtes
    /// comparent sur <c>ToUpper()</c> plutôt que de se fier à la collation de la base.
    /// </summary>
    public static readonly string[] Generic = Unnamed.Concat(Institutional).ToArray();

    /// <summary>Le libellé ne nomme personne : à remplacer à l'affichage.</summary>
    public static bool IsUnnamed(string? company) => Matches(Unnamed, company);

    /// <summary>Le libellé ne doit pas former de fiche entreprise.</summary>
    public static bool IsGeneric(string? company) => Matches(Generic, company);

    private static bool Matches(string[] set, string? company) =>
        company is not null && set.Contains(company.Trim().ToUpperInvariant());
}
