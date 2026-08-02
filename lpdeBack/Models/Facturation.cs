namespace lpdeBack.Models;

/// <summary>
/// Ce qu'un recruteur a le droit de faire, et ce qu'il a paye pour.
///
/// La mise en avant d'une offre existait deja — bouton « Sponsoriser »,
/// etiquette sur la carte, remontee dans le tri — mais elle etait
/// gratuite et sans limite. Autrement dit, le seul levier economique du
/// site etait offert, et comme tout le monde pouvait s'en servir, il ne
/// distinguait plus rien : quand toutes les offres sont mises en avant,
/// aucune ne l'est.
///
/// Ce fichier rassemble les trois objets qui manquaient : la formule
/// souscrite, l'achat ponctuel d'une mise en avant, et la facture qui
/// en decoule.
/// </summary>
public class Abonnement
{
    public int Id { get; set; }

    /// <summary>Le recruteur, ou l'entreprise si la formule est prise au niveau de l'equipe.</summary>
    public string UserId { get; set; } = string.Empty;
    public string? Entreprise { get; set; }

    /// <summary>« gratuit », « essentiel », « pro ». Voir <see cref="Formules"/>.</summary>
    public string Formule { get; set; } = "gratuit";

    public DateTime DebutLe { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Nul pour la formule gratuite, qui n'expire pas. Une formule
    /// echue retombe sur la gratuite sans rien supprimer : les offres
    /// deja publiees restent en ligne, seule la publication d'une
    /// nouvelle est refusee au-dela du quota.
    /// </summary>
    public DateTime? FinLe { get; set; }

    /// <summary>« actif », « annule », « impaye ».</summary>
    public string Statut { get; set; } = "actif";

    /// <summary>Reference chez le prestataire de paiement, quand il y en a un.</summary>
    public string? ReferenceExterne { get; set; }
}

/// <summary>
/// Les formules et ce qu'elles ouvrent.
///
/// Elles vivent dans le code et non en base : ce sont des regles
/// commerciales, elles changent par deploiement et non par clic, et
/// les voir ici evite de chercher en base pourquoi un recruteur est
/// bloque a trois offres.
/// </summary>
public static class Formules
{
    /// <param name="PrixMensuelCentimes">En centimes, par mois. Zero pour la formule gratuite.</param>
    /// <param name="OffresActives">Offres actives simultanees. -1 pour illimite.</param>
    /// <param name="AccesVivier">Acces au vivier de candidats.</param>
    /// <param name="MisesEnAvantIncluses">Mises en avant incluses par mois.</param>
    public record Definition(
        string Cle,
        string Nom,
        int PrixMensuelCentimes,
        int OffresActives,
        bool AccesVivier,
        int MisesEnAvantIncluses,
        string[] Arguments);

    public static readonly Definition Gratuit = new(
        "gratuit", "Gratuit", 0, 3, false, 0,
        new[]
        {
            "3 offres en ligne simultanement",
            "Candidatures et messagerie sans limite",
            "Page entreprise et statistiques par offre",
        });

    public static readonly Definition Essentiel = new(
        "essentiel", "Essentiel", 4900, 15, true, 1,
        new[]
        {
            "15 offres en ligne simultanement",
            "Acces au vivier de candidats",
            "1 mise en avant incluse par mois",
            "Modeles d'offres et reponses automatiques",
        });

    public static readonly Definition Pro = new(
        "pro", "Pro", 14900, -1, true, 5,
        new[]
        {
            "Offres illimitees",
            "Acces au vivier de candidats",
            "5 mises en avant incluses par mois",
            "Comptes d'equipe et offres partagees",
            "Acces a l'API et aux webhooks",
        });

    public static readonly Definition[] Toutes = { Gratuit, Essentiel, Pro };

    public static Definition Par(string? cle) =>
        Toutes.FirstOrDefault(f => f.Cle == (cle ?? "gratuit")) ?? Gratuit;
}

/// <summary>
/// Une mise en avant achetee a l'unite, hors formule.
///
/// Separee de l'abonnement parce qu'elle repond a un autre besoin :
/// pousser une offre difficile a pourvoir sans changer de formule pour
/// autant.
/// </summary>
public class MiseEnAvant
{
    public int Id { get; set; }
    public int JobOfferId { get; set; }
    public string UserId { get; set; } = string.Empty;

    public DateTime DebutLe { get; set; } = DateTime.UtcNow;
    public DateTime FinLe { get; set; }

    /// <summary>En centimes. Zero si elle etait incluse dans la formule.</summary>
    public int MontantCentimes { get; set; }

    /// <summary>« incluse », « payee », « en_attente ».</summary>
    public string Origine { get; set; } = "incluse";

    public string? ReferenceExterne { get; set; }
}

/// <summary>
/// La facture.
///
/// Elle n'est pas un confort : un professionnel qui paie doit pouvoir
/// justifier la depense, et la mention de TVA n'est pas facultative.
/// Le numero est sequentiel et sans trou — c'est une obligation
/// comptable, et c'est pourquoi il est attribue a l'emission et jamais
/// reutilise, meme si la facture est annulee ensuite.
/// </summary>
public class Facture
{
    public int Id { get; set; }

    /// <summary>Format « F-2026-000123 ». Sequentiel, sans trou, jamais reattribue.</summary>
    public string Numero { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    /// <summary>Fige a l'emission : une facture ne change pas quand le client demenage.</summary>
    public string? RaisonSociale { get; set; }
    public string? AdresseFacturation { get; set; }
    public string? NumeroTva { get; set; }

    public string Libelle { get; set; } = string.Empty;

    /// <summary>Tout en centimes : les flottants n'ont rien a faire dans une addition d'argent.</summary>
    public int MontantHtCentimes { get; set; }
    public int TvaCentimes { get; set; }
    public int MontantTtcCentimes { get; set; }

    /// <summary>En points de pourcentage (2000 = 20,00 %).</summary>
    public int TauxTvaMillimes { get; set; } = 2000;

    /// <summary>« emise », « payee », « annulee ».</summary>
    public string Statut { get; set; } = "emise";

    public DateTime EmiseLe { get; set; } = DateTime.UtcNow;
    public DateTime? PayeeLe { get; set; }

    public string? ReferenceExterne { get; set; }
}
