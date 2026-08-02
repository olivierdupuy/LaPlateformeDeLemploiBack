namespace lpdeBack.Models;

/// <summary>
/// Signalement au titre du reglement europeen sur les services
/// numeriques (DSA, article 16).
///
/// Les mentions legales renvoyaient vers une adresse de courriel. Ce
/// n'est pas ce que le texte demande : il exige un mecanisme
/// « electronique, facile d'acces et convivial », un accuse de
/// reception, une decision motivee, et l'indication des voies de
/// recours. Une boite aux lettres ne fournit aucun des quatre.
///
/// Le signalement est ouvert sans compte — c'est la condition pour qu'il
/// compte. En echange, il porte une reference que le declarant conserve
/// et qui lui permet de suivre l'instruction sans s'identifier.
/// </summary>
public class SignalementDsa
{
    public int Id { get; set; }

    /// <summary>
    /// Reference courte remise au declarant. C'est le seul lien entre
    /// lui et son dossier quand il n'a pas de compte.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Ce qui est signale : « offre », « avis », « message », « profil », « autre ».</summary>
    public string TypeContenu { get; set; } = "offre";

    /// <summary>Identifiant du contenu vise quand il en a un.</summary>
    public string? ContenuId { get; set; }

    /// <summary>Adresse de la page ou le contenu a ete vu.</summary>
    public string? Url { get; set; }

    /// <summary>
    /// Categorie d'illiceite invoquee. Le texte demande que le declarant
    /// puisse dire en quoi le contenu est illicite, pas seulement qu'il
    /// lui deplait.
    /// </summary>
    public string Motif { get; set; } = string.Empty;

    /// <summary>L'expose des faits, en clair.</summary>
    public string Explication { get; set; } = string.Empty;

    /// <summary>
    /// Adresse du declarant. Facultative : le reglement admet le
    /// signalement anonyme, sauf pour les infractions les plus graves.
    /// Sans elle, l'accuse de reception et la decision ne peuvent pas
    /// etre transmis — le declarant en est prevenu.
    /// </summary>
    public string? EmailDeclarant { get; set; }

    /// <summary>
    /// Declaration de bonne foi. Le texte lui donne un effet : elle
    /// engage le declarant sur l'exactitude de ce qu'il affirme.
    /// </summary>
    public bool DeclareBonneFoi { get; set; }

    /// <summary>« Recu », « EnCours », « Fonde », « NonFonde ».</summary>
    public string Statut { get; set; } = "Recu";

    /// <summary>
    /// La motivation de la decision, transmise au declarant. Le
    /// reglement ne se satisfait pas d'un « rejete » : il exige les
    /// raisons.
    /// </summary>
    public string? Decision { get; set; }

    /// <summary>Ce qui a ete fait : « Aucune », « ContenuRetire », « CompteSuspendu ».</summary>
    public string? MesurePrise { get; set; }

    public DateTime CreeLe { get; set; } = DateTime.UtcNow;
    public DateTime? TraiteLe { get; set; }

    /// <summary>Qui a instruit, cote administration.</summary>
    public string? TraitePar { get; set; }
}
