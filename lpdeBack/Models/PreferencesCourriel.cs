namespace lpdeBack.Models;

/// <summary>
/// Ce que quelqu'un accepte de recevoir.
///
/// Il n'y avait qu'un interrupteur, et il ne portait que sur la lettre
/// d'information. Tout le reste — alertes d'offres, accuses de
/// candidature, messages de recruteurs, rappels d'entretien — partait
/// sans que personne ne puisse en retrancher une categorie. Qui recevait
/// trop d'alertes n'avait qu'un seul geste a sa disposition : nous
/// signaler comme indesirable, ce qui emporte avec lui les messages
/// qu'il voulait vraiment.
///
/// Les preferences sont indexees par adresse et non par compte : le lien
/// de gestion arrive dans un courriel, et exiger une connexion pour
/// cesser de recevoir des courriels est precisement ce qui pousse au
/// bouton « indesirable ».
///
/// Trois envois ne sont pas negociables et n'apparaissent pas ici :
/// reinitialisation de mot de passe, confirmation d'adresse, alerte de
/// connexion inhabituelle. Ils repondent a une action de la personne ou
/// protegent son compte ; les couper serait lui nuire.
/// </summary>
public class PreferencesCourriel
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Jeton du lien de gestion, place dans le pied de chaque courriel.
    /// Il ouvre les preferences de cette adresse et rien d'autre.
    /// </summary>
    public string Jeton { get; set; } = string.Empty;

    /// <summary>Les offres correspondant aux recherches enregistrees.</summary>
    public bool AlertesOffres { get; set; } = true;

    /// <summary>Accuse de reception, changement de statut, relance.</summary>
    public bool SuiviCandidatures { get; set; } = true;

    /// <summary>Un recruteur ou un candidat a ecrit.</summary>
    public bool Messages { get; set; } = true;

    /// <summary>Invitation, confirmation, rappel la veille.</summary>
    public bool Entretiens { get; set; } = true;

    /// <summary>La lettre d'information.</summary>
    public bool LettreInformation { get; set; } = true;

    /// <summary>Nouveautes du site, enquetes. Le seul dont le defaut est « non ».</summary>
    public bool Actualites { get; set; }

    /// <summary>
    /// Coupe tout ce qui est coupable, d'un geste. Redondant avec les
    /// six precedents, et c'est voulu : quelqu'un qui veut que ca cesse
    /// ne doit pas avoir a decocher six cases.
    /// </summary>
    public bool ToutRefuse { get; set; }

    public DateTime MisAJourLe { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Une adresse qui ne repond plus.
///
/// On continuait d'ecrire a des adresses mortes — demission, boite
/// fermee, faute de frappe a l'inscription. Chaque rejet abime la
/// reputation du domaine expediteur, et la reputation abimee fait
/// tomber en indesirable les courriels qui, eux, etaient attendus : les
/// mots de passe oublies, les confirmations d'adresse. Un site dont la
/// reinitialisation de mot de passe n'arrive plus est un site ou l'on ne
/// peut plus entrer.
/// </summary>
public class RetourCourriel
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// « dur » : l'adresse n'existe pas, on cesse immediatement.
    /// « doux » : boite pleine ou serveur indisponible, on tolere.
    /// « plainte » : la personne a cliqué sur « indesirable » — c'est le
    /// plus grave, et on cesse tout, y compris la lettre.
    /// </summary>
    public string Type { get; set; } = "dur";

    public string? Motif { get; set; }

    /// <summary>Rejets successifs. Trois retours doux valent un dur.</summary>
    public int Occurrences { get; set; } = 1;

    /// <summary>Vrai quand on a cesse d'ecrire a cette adresse.</summary>
    public bool Bloque { get; set; }

    public DateTime PremierLe { get; set; } = DateTime.UtcNow;
    public DateTime DernierLe { get; set; } = DateTime.UtcNow;
}
