using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>
/// Un mot laisse sur une candidature, visible de toute l'equipe.
///
/// « Application.RecruiterNotes » existait deja, mais c'est un champ
/// unique : le second qui ecrit efface le premier, et rien ne dit qui a
/// ecrit quoi ni quand. Deux recruteurs sur le meme dossier s'effacaient
/// donc mutuellement sans jamais s'en apercevoir.
///
/// Ces notes-ci s'empilent et portent leur auteur. Elles ne remplacent
/// pas l'ancien champ — qui reste le brouillon personnel attache au
/// dossier — elles ajoutent la conversation d'equipe qui manquait.
///
/// ELLES NE SORTENT JAMAIS COTE CANDIDAT
/// C'est la meme regle que pour « RecruiterNotes », que le suivi de
/// candidature exclut explicitement. Un avis ecrit entre collegues n'est
/// pas destine a la personne dont on parle, et rien dans l'ecran ne
/// laisse penser le contraire a celui qui l'ecrit.
/// </summary>
public class NoteCandidature
{
    public int Id { get; set; }

    public int ApplicationId { get; set; }
    public Application Application { get; set; } = null!;

    [MaxLength(450)]
    public string? AuteurId { get; set; }

    /// <summary>Le nom de l'auteur, fige a l'ecriture.</summary>
    [MaxLength(200)]
    public string AuteurNom { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Contenu { get; set; } = string.Empty;

    public DateTime CreeLe { get; set; } = DateTime.UtcNow;
}
