namespace lpdeBack.Models;

/// <summary>
/// Une offre poussee vers un partenaire.
///
/// Le catalogue sortait deja par des flux — XML pour les agregateurs,
/// JSON-LD pour Google for Jobs — mais un flux se subit : le partenaire
/// vient le lire quand il veut, on ne sait pas ce qu'il en fait, et
/// retirer une offre pourvue consiste a esperer qu'il repassera. Pour
/// une offre que le recruteur veut voir partout tout de suite, c'est
/// insuffisant.
///
/// La multidiffusion est l'inverse : on pousse, on recoit une reference,
/// et on peut retirer. C'est cette reference qui compte — sans elle, on
/// ne sait pas quoi retirer, et une offre pourvue continue de recevoir
/// des candidatures ailleurs pendant des semaines. C'est le reproche le
/// plus courant fait aux sites d'emploi, et il est merite.
/// </summary>
public class Diffusion
{
    public int Id { get; set; }

    public int JobOfferId { get; set; }
    public JobOffer? JobOffer { get; set; }

    /// <summary>Qui a demande la diffusion. Sert au perimetre et au journal.</summary>
    public string DemandeeParUserId { get; set; } = string.Empty;

    /// <summary>La cle du partenaire : « france-travail », « partenaire-xml »…</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// « en_attente », « diffusee », « echec », « retiree ».
    ///
    /// L'echec est un etat et non une exception avalee : une diffusion
    /// qui n'aboutit pas doit se voir dans la console, avec son motif,
    /// sinon le recruteur croit son offre partie.
    /// </summary>
    public string Statut { get; set; } = "en_attente";

    /// <summary>L'identifiant rendu par le partenaire. Sans lui, pas de retrait.</summary>
    public string? ReferenceExterne { get; set; }

    /// <summary>L'adresse publique de l'offre chez le partenaire, si elle est connue.</summary>
    public string? UrlExterne { get; set; }

    /// <summary>Le motif du dernier echec, tel qu'il sera montre au recruteur.</summary>
    public string? Motif { get; set; }

    public DateTime DemandeeLe { get; set; } = DateTime.UtcNow;
    public DateTime? DiffuseeLe { get; set; }
    public DateTime? RetireeLe { get; set; }

    /// <summary>
    /// Combien de fois on a essaye.
    ///
    /// Un partenaire indisponible ne doit pas etre retente indefiniment :
    /// au-dela du plafond, la diffusion reste en echec et attend une
    /// reprise a la main. Reessayer sans fin transforme une panne chez
    /// eux en charge chez nous.
    /// </summary>
    public int Tentatives { get; set; }
}
