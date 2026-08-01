using System.ComponentModel.DataAnnotations;

namespace lpdeBack.Models;

/// <summary>
/// Un abonne a la lettre d'information.
///
/// La table existe meme pour les membres inscrits : un compte ne vaut pas
/// consentement a recevoir du courrier commercial, et la CNIL demande de
/// pouvoir prouver ce consentement — quand, depuis ou, et par quel geste.
/// Un booleen sur AppUser ne prouverait rien.
///
/// Les visiteurs sans compte y figurent au meme titre. Une seule table,
/// donc un seul endroit ou verifier qu'une adresse a bien accepte, et un
/// seul endroit ou la retirer.
/// </summary>
public class NewsletterSubscriber
{
    public int Id { get; set; }

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(100)] public string? FirstName { get; set; }
    [MaxLength(100)] public string? LastName { get; set; }

    /// <summary>Le compte, quand l'abonne en a un. Nul pour un visiteur.</summary>
    [MaxLength(450)] public string? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Pending, Confirmed, Unsubscribed, Bounced.</summary>
    [MaxLength(20)] public string Status { get; set; } = "Pending";

    /// <summary>
    /// Le jeton du lien de confirmation. Efface une fois servi : un lien de
    /// confirmation qui reste valable indefiniment permet de reactiver un
    /// abonnement que quelqu'un vient de resilier.
    /// </summary>
    [MaxLength(64)] public string? ConfirmToken { get; set; }
    public DateTime? ConfirmTokenSentAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>
    /// Le jeton de desinscription, lui, ne change jamais et ne s'efface pas :
    /// il voyage dans chaque message deja parti, et doit fonctionner des
    /// annees plus tard. C'est la loi, et c'est aussi ce qui evite les
    /// plaintes pour pourriel.
    /// </summary>
    [MaxLength(64)] public string UnsubscribeToken { get; set; } = string.Empty;
    public DateTime? UnsubscribedAt { get; set; }
    [MaxLength(200)] public string? UnsubscribeReason { get; set; }

    // ── Preuve du consentement ──
    public DateTime ConsentAt { get; set; } = DateTime.UtcNow;
    [MaxLength(64)] public string? ConsentIp { get; set; }
    /// <summary>D'ou vient l'abonnement : Footer, Page, Inscription, Admin, Import.</summary>
    [MaxLength(30)] public string Source { get; set; } = "Footer";

    // ── De quoi cibler ──
    /// <summary>Categories de metier suivies, separees par des virgules.</summary>
    [MaxLength(400)] public string? Categories { get; set; }
    [MaxLength(120)] public string? City { get; set; }
    /// <summary>Deduit de la ville quand elle porte un code : « 34 - Montpellier ».</summary>
    [MaxLength(5)] public string? Department { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSentAt { get; set; }

    /// <summary>Combien de messages ont echoue de suite. Trois : on cesse d'ecrire.</summary>
    public int ConsecutiveFailures { get; set; }

    public bool EstJoignable => Status == "Confirmed" && UnsubscribedAt == null;
}

/// <summary>
/// Une campagne : ce qu'on ecrit, a qui, et ce qu'il en est advenu.
///
/// Le corps est conserve tel qu'il a ete envoye. Le modifier apres coup
/// rendrait les statistiques incomprehensibles — on ne saurait plus a quoi
/// les gens ont repondu.
/// </summary>
public class NewsletterCampaign
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// La ligne d'apercu, affichee par les messageries a cote de l'objet.
    /// Sans elle, elles montrent les premiers mots du corps — souvent
    /// « Voir ce message dans votre navigateur ».
    /// </summary>
    [MaxLength(200)] public string? PreviewText { get; set; }

    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>Draft, Sending, Sent, Failed, Cancelled.</summary>
    [MaxLength(20)] public string Status { get; set; } = "Draft";

    // ── Le segment vise, garde tel qu'il a servi ──
    /// <summary>Roles vises, separes par des virgules : Candidate, Recruiter, Guest.</summary>
    [MaxLength(100)] public string? SegmentRoles { get; set; }
    [MaxLength(400)] public string? SegmentCategories { get; set; }
    [MaxLength(400)] public string? SegmentCities { get; set; }
    [MaxLength(200)] public string? SegmentDepartments { get; set; }
    /// <summary>Tous, Recents (inscrits depuis moins de 30 j), Dormants (sans connexion depuis 90 j).</summary>
    [MaxLength(20)] public string? SegmentActivity { get; set; }

    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    [MaxLength(200)] public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    // ── Ce qu'il en est advenu ──
    public int Recipients { get; set; }
    public int Delivered { get; set; }
    public int Failed { get; set; }

    public ICollection<NewsletterDelivery> Deliveries { get; set; } = new List<NewsletterDelivery>();
}

/// <summary>
/// Une ligne par destinataire et par campagne.
///
/// Elle sert deux fois : a rendre compte, et a ne jamais ecrire deux fois
/// a la meme personne. Un envoi interrompu par un redemarrage reprend ou
/// il s'etait arrete, sans redoubler les messages deja partis — ce qu'un
/// simple compteur sur la campagne ne permettrait pas.
/// </summary>
public class NewsletterDelivery
{
    public int Id { get; set; }

    public int CampaignId { get; set; }
    public NewsletterCampaign? Campaign { get; set; }

    public int SubscriberId { get; set; }
    public NewsletterSubscriber? Subscriber { get; set; }

    /// <summary>L'adresse au moment de l'envoi : elle a pu changer depuis.</summary>
    [MaxLength(256)] public string Email { get; set; } = string.Empty;

    /// <summary>Pending, Sent, Failed.</summary>
    [MaxLength(20)] public string Status { get; set; } = "Pending";

    public DateTime? SentAt { get; set; }
    [MaxLength(300)] public string? Error { get; set; }

    /// <summary>L'identifiant rendu par Brevo, pour retrouver un message chez eux.</summary>
    [MaxLength(200)] public string? ProviderMessageId { get; set; }
}
