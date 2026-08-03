namespace lpdeBack.Models;

/// <summary>
/// Les deux roles d'une equipe de recrutement.
///
/// Deux et pas davantage. Un modele par capacite — publier, moderer,
/// facturer, cochees une a une — se defend sur le papier et se paie a
/// l'usage : il faut l'administrer, et personne ne le fait. Deux roles
/// couvrent le cas reel, qui est « qui peut toucher aux offres des
/// autres ».
/// </summary>
public static class RolesEquipe
{
    /// <summary>Gere les offres de toute l'equipe, et distribue les roles.</summary>
    public const string Proprietaire = "proprietaire";

    /// <summary>Gere ses propres offres, lit celles des autres.</summary>
    public const string Membre = "membre";

    public static readonly string[] Tous = { Proprietaire, Membre };

    public static bool Existe(string? r) => r is not null && Tous.Contains(r);

    public static string Libelle(string? r) => r switch
    {
        Proprietaire => "propriétaire",
        Membre => "membre",
        _ => r ?? "",
    };
}
