using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>
/// L'etat de publication d'une offre, du point de vue du recruteur.
///
/// « IsActive » disait si le public voit l'annonce. Il ne disait pas
/// pourquoi il ne la voit plus, et les deux raisons n'appellent pas la
/// meme suite : une offre suspendue le temps d'un arbitrage reviendra,
/// une offre fermee est finie. Faute de les distinguer, il fallait
/// supprimer l'annonce — en emportant ses candidatures — ou la laisser
/// tourner et recevoir des dossiers qu'on ne traiterait pas.
///
/// Les valeurs sont en francais sans accent, comme « Diffusion.Statut »
/// et les autres etats internes recents. Elles ne sortent pas dans l'API
/// publique, ou seul « isActive » circule.
/// </summary>
public static class EtatOffre
{
    public const string Ouverte = "ouverte";
    public const string Suspendue = "suspendue";
    public const string Fermee = "fermee";

    public static readonly string[] Tous = { Ouverte, Suspendue, Fermee };

    public static bool Existe(string? etat) => etat is not null && Tous.Contains(etat);

    public static string Libelle(string? etat) => etat switch
    {
        Ouverte => "ouverte",
        Suspendue => "suspendue",
        Fermee => "fermée",
        _ => etat ?? "",
    };

    /// <summary>
    /// Pose l'etat et la visibilite d'un seul geste.
    ///
    /// Le seul endroit ou l'invariant s'ecrit. Laisser chaque appelant
    /// mettre a jour les deux champs reviendrait a attendre qu'un seul
    /// oublie l'un des deux : une offre affichee « fermee » au recruteur
    /// et toujours visible du public, ou l'inverse — et rien, dans les
    /// deux cas, ne le signalerait.
    /// </summary>
    public static void Appliquer(JobOffer offre, string etat)
    {
        offre.EtatPublication = etat;
        offre.IsActive = etat == Ouverte;
    }

    /// <summary>
    /// L'etat qui correspond a une visibilite, pour les chemins qui
    /// raisonnent encore en booleen — la creation, la moderation,
    /// l'expiration automatique.
    ///
    /// Une offre qu'on rend invisible sans le dire est fermee et non
    /// suspendue : la suspension est une intention, et une intention se
    /// declare. La reprise, elle, rouvre — on ne remet pas en ligne une
    /// annonce pour la laisser en pause.
    /// </summary>
    public static void Appliquer(JobOffer offre, bool visible) =>
        Appliquer(offre, visible ? Ouverte : Fermee);
}

/// <summary>Un etat de publication, et rien d'autre.</summary>
public sealed class EtatOffreAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        var s = (value as string ?? "").Trim();
        return s.Length == 0 || EtatOffre.Existe(s);
    }

    public override string FormatErrorMessage(string name) =>
        $"État inattendu. Choisissez parmi : {string.Join(", ", EtatOffre.Tous)}.";
}
