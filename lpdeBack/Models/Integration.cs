namespace lpdeBack.Models;

/// <summary>
/// Une cle d'acces a l'API, remise a un recruteur.
///
/// Les recruteurs equipes d'un logiciel de recrutement ne veulent pas
/// ressaisir leurs offres chez nous : ils veulent que leur outil le
/// fasse. Sans cle, la seule facon de s'interfacer etait de piloter un
/// navigateur — ce que certains font, et qui casse a chaque changement
/// d'ecran.
///
/// La cle n'est **pas** stockee. Seule son empreinte l'est, comme un
/// mot de passe : une base qui fuit ne doit pas livrer des acces en
/// clair. Le porteur la voit une fois, a la creation, et ne la reverra
/// jamais.
/// </summary>
public class JetonApi
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    /// <summary>Nom donne par le porteur : « Teamtailor », « script interne ».</summary>
    public string Nom { get; set; } = string.Empty;

    /// <summary>
    /// Les huit premiers caracteres, en clair. Ils ne permettent rien et
    /// servent a reconnaitre une cle dans une liste — sans eux, revoquer
    /// la bonne parmi cinq releve du tirage au sort.
    /// </summary>
    public string Prefixe { get; set; } = string.Empty;

    /// <summary>SHA-256 de la cle complete.</summary>
    public string Empreinte { get; set; } = string.Empty;

    /// <summary>Portees accordees, separees par des virgules : « offres:lire,offres:ecrire ».</summary>
    public string Portees { get; set; } = "offres:lire";

    public DateTime CreeLe { get; set; } = DateTime.UtcNow;
    public DateTime? DerniereUtilisation { get; set; }
    public DateTime? RevoqueLe { get; set; }
}

/// <summary>
/// Un abonnement a nos evenements.
///
/// Un recruteur equipe veut savoir qu'une candidature est arrivee sans
/// interroger l'API toutes les minutes. L'interrogation en boucle coute
/// aux deux parties et arrive toujours en retard ; le webhook coute une
/// requete par evenement reel.
/// </summary>
public class Webhook
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary>Evenements souscrits, separes par des virgules.</summary>
    public string Evenements { get; set; } = "candidature.creee";

    /// <summary>
    /// Secret partage. Chaque livraison porte une signature HMAC-SHA256
    /// du corps ; sans elle, n'importe qui connaissant l'URL pourrait
    /// fabriquer de fausses notifications.
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    public bool Actif { get; set; } = true;

    /// <summary>
    /// Echecs consecutifs. Au-dela d'un seuil, l'abonnement se desactive
    /// tout seul : continuer a frapper une URL morte pendant des mois
    /// use nos files et le serveur d'en face.
    /// </summary>
    public int EchecsConsecutifs { get; set; }

    public DateTime CreeLe { get; set; } = DateTime.UtcNow;
    public DateTime? DerniereLivraison { get; set; }
    public string? DerniereErreur { get; set; }
}

/// <summary>
/// Une livraison de webhook, gardee pour qu'on puisse repondre a
/// « je n'ai rien recu ».
/// </summary>
public class LivraisonWebhook
{
    public int Id { get; set; }
    public int WebhookId { get; set; }

    public string Evenement { get; set; } = string.Empty;
    public string Charge { get; set; } = string.Empty;

    public int? CodeReponse { get; set; }
    public string? Erreur { get; set; }
    public int Tentatives { get; set; }

    public DateTime CreeLe { get; set; } = DateTime.UtcNow;
    public DateTime? LivreLe { get; set; }
}
