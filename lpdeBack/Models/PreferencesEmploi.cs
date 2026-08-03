using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>
/// Ce que le candidat cherche, dit par lui.
///
/// Jusqu'ici la correspondance offre / candidat devinait ces souhaits en
/// lisant la derniere recherche enregistree. C'etait la meilleure source
/// disponible et elle avait sa logique — une recherche qu'on prend la
/// peine d'enregistrer est une declaration d'intention — mais elle se
/// trompe de deux facons.
///
/// Elle est muette : quatre-vingt-dix pour cent des candidats n'ont
/// jamais enregistre de recherche, et pour eux la correspondance se
/// calculait sans contrat vise, sans envie de teletravail, sans plancher
/// de salaire. Trois criteres sur sept ne pesaient rien, ce qui abaisse
/// la fiabilite affichee sans que personne ne sache pourquoi.
///
/// Et elle est bavarde a contretemps : une recherche faite un soir pour
/// un ami, ou par curiosite sur un autre metier, devenait un souhait
/// permanent que rien ne permettait de corriger.
///
/// Ces preferences-ci sont declarees, modifiables, et affichees a cote du
/// score qu'elles produisent. Un candidat qui ne comprend pas une
/// correspondance peut voir ce sur quoi elle repose et le changer.
///
/// LE CHOIX DES CHAMPS
/// Quatre, et pas cinq. « Horaires » figurait dans le cahier des charges
/// et n'y est pas : le moteur de correspondance note sept criteres dont
/// aucun ne regarde le rythme de travail, et l'ajouter obligerait a
/// redistribuer des poids qui totalisent cent et que des tests figent.
/// Stocker un champ que rien ne lit serait pire que de ne pas l'avoir —
/// c'est exactement le reproche fait aux preferences qui ne servent a
/// rien. A reprendre le jour ou le moteur saura le peser.
/// </summary>
public class PreferencesEmploi
{
    public int Id { get; set; }

    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Brut annuel, en euros. Le moteur ramene tout a l'annee — une offre
    /// affichee au mois ou a l'heure est convertie avant comparaison.
    /// </summary>
    public int? SalaireAnnuelMinimum { get; set; }

    /// <summary>« CDI », « CDD », « Alternance »… Nul si indifferent.</summary>
    [MaxLength(40)]
    public string? Contrat { get; set; }

    /// <summary>
    /// Vrai si le teletravail est recherche. Nul veut dire indifferent, et
    /// ce n'est pas la meme chose que faux : « peu importe » ne doit pas
    /// penaliser une offre a distance.
    /// </summary>
    public bool? Distanciel { get; set; }

    /// <summary>
    /// Jusqu'ou le candidat accepte de se deplacer, depuis la ville de son
    /// profil. Nul si aucune limite declaree.
    /// </summary>
    public int? RayonKm { get; set; }

    public DateTime MisAJourLe { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Vrai si rien n'a ete renseigne. Un enregistrement vide existe des
    /// que le candidat ouvre le formulaire et le referme : il ne doit pas
    /// pour autant faire taire le repli sur la derniere recherche.
    /// </summary>
    public bool EstVide =>
        SalaireAnnuelMinimum is null && Contrat is null && Distanciel is null && RayonKm is null;
}
