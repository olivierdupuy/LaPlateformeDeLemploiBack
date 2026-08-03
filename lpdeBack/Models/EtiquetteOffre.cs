using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>
/// Un mot que le recruteur pose sur une de ses offres, pour s'y retrouver.
///
/// « campagne printemps », « priorite direction », « a revoir » : du
/// vocabulaire interne, qui n'a de sens que pour l'equipe qui l'ecrit.
///
/// POURQUOI UNE TABLE ET NON UNE COLONNE
/// « JobOffers.Tags » existe deja et aurait fait l'affaire — sauf que le
/// point d'entree public rend l'entite « JobOffer » entiere. Une colonne
/// de plus, et « priorite direction » partait dans la charge utile de
/// chaque visiteur du catalogue. Une table separee ne peut pas fuir par
/// accident : il faut ecrire la jointure pour la publier.
///
/// L'ETIQUETTE APPARTIENT A L'OFFRE, PAS A LA PERSONNE
/// Une equipe partage ses offres ; elle doit partager la facon de les
/// ranger. Deux recruteurs de la meme maison voient les memes etiquettes,
/// et « CreeParUserId » ne sert qu'a savoir qui a pose le mot — jamais a
/// filtrer ce que l'autre voit.
/// </summary>
public class EtiquetteOffre
{
    public int Id { get; set; }

    public int JobOfferId { get; set; }
    public JobOffer JobOffer { get; set; } = null!;

    /// <summary>
    /// Le mot, tel qu'il a ete tape. Conserve sa casse pour l'affichage,
    /// mais l'unicite se juge sans elle : « Urgent » et « urgent » sont la
    /// meme etiquette, et en avoir deux serait une facon de perdre la
    /// moitie de ses offres au filtrage.
    /// </summary>
    [MaxLength(40)]
    public string Nom { get; set; } = string.Empty;

    /// <summary>La forme repliee, qui porte l'unicite.</summary>
    [MaxLength(40)]
    public string Cle { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? CreeParUserId { get; set; }

    public DateTime CreeLe { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// La forme repliee d'un mot : sans casse, sans espaces de bord.
    /// Les accents sont conserves — « déménagement » et « demenagement »
    /// sont deux mots qu'un recruteur ecrit rarement l'un pour l'autre, et
    /// les confondre reviendrait a renommer son etiquette dans son dos.
    /// </summary>
    public static string Replier(string nom) => nom.Trim().ToLowerInvariant();
}
