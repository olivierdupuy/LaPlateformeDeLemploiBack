using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>
/// Les etats par lesquels passe une candidature.
///
/// La liste des valeurs admises etait recopiee dans quatre controleurs et
/// deux attributs de validation. Six endroits a tenir d'accord a la main,
/// et le premier oubli n'aurait rien casse de visible : un statut refuse
/// ici et accepte la, selon le chemin emprunte. Elle vit desormais ici, et
/// nulle part ailleurs.
///
/// LES DEUX ETATS AJOUTES
/// « Contactee » manquait entre « examinee » et « acceptee ». C'est
/// pourtant l'etat reel de la plupart des candidatures pendant la plus
/// grande partie de leur vie : le recruteur a ecrit, il attend une
/// reponse, et rien ne le distinguait d'un dossier simplement lu.
///
/// « Embauchee » ferme la boucle. Sans lui, « acceptee » servait a la fois
/// pour « je retiens votre candidature » et pour « vous commencez lundi »,
/// et aucun delai d'embauche n'etait calculable.
///
/// LES CHAINES RESTENT EN ANGLAIS
/// Elles sont en base sur des milliers de lignes et voyagent dans l'API
/// publique, que des integrateurs consomment. Les renommer serait une
/// rupture de contrat pour un gain d'elegance interne. Les libelles
/// francais sont rendus par « Libelle ».
/// </summary>
public static class StatutCandidature
{
    public const string EnAttente = "Pending";
    public const string Examinee = "Reviewed";
    public const string Contactee = "Contacted";
    public const string Acceptee = "Accepted";
    public const string Embauchee = "Hired";
    public const string Refusee = "Rejected";

    /// <summary>
    /// Dans l'ordre du parcours, le refus mis a part : il peut survenir a
    /// n'importe quelle etape et ne suit donc personne.
    /// </summary>
    public static readonly string[] Tous =
    {
        EnAttente, Examinee, Contactee, Acceptee, Embauchee, Refusee,
    };

    /// <summary>
    /// Ce qui reste en jeu. Sert a compter les candidatures qui dorment et
    /// a savoir lesquelles meritent une relance — une candidature refusee
    /// ou une embauche conclue n'attendent plus rien.
    /// </summary>
    public static readonly string[] EnCours =
    {
        EnAttente, Examinee, Contactee,
    };

    /// <summary>Les etats dont on ne revient pas.</summary>
    public static bool EstTermine(string? statut) =>
        statut == Embauchee || statut == Refusee;

    /// <summary>Le libelle francais, tel qu'il s'affiche.</summary>
    public static string Libelle(string? statut) => statut switch
    {
        EnAttente => "en attente",
        Examinee => "examinée",
        Contactee => "contactée",
        Acceptee => "acceptée",
        Embauchee => "embauchée",
        Refusee => "refusée",
        _ => statut ?? "",
    };

    public static bool Existe(string? statut) =>
        statut is not null && Tous.Contains(statut);
}

/// <summary>
/// Un statut de candidature, et rien d'autre.
///
/// « [Parmi(...)] » demandait la liste en clair a chaque usage, ce qu'un
/// attribut impose : ses arguments doivent etre des constantes de
/// compilation, un tableau statique n'y passe pas. Cet attribut-ci lit la
/// liste centrale a l'execution, ou cette contrainte n'existe plus.
/// </summary>
public sealed class StatutCandidatureAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        var s = (value as string ?? "").Trim();
        return s.Length == 0 || StatutCandidature.Existe(s);
    }

    public override string FormatErrorMessage(string name) =>
        $"Statut inattendu. Choisissez parmi : {string.Join(", ", StatutCandidature.Tous)}.";
}
