using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Prevenir les systemes tiers de ce qui se passe chez nous.
///
/// Un recruteur equipe d'un logiciel de recrutement veut savoir qu'une
/// candidature est arrivee sans interroger l'API toutes les minutes.
/// L'interrogation en boucle coute aux deux parties et arrive toujours
/// en retard ; le webhook coute une requete par evenement reel.
///
/// Trois choses qu'on ne peut pas se permettre d'oublier ici :
///
///   **Signer.** Sans signature, quiconque connait l'URL peut fabriquer
///   de fausses notifications — « ce candidat a ete embauche »,
///   « cette offre est fermee ». Chaque livraison porte un HMAC-SHA256
///   du corps, avec l'horodatage dans la signature pour qu'une
///   livraison capturee ne puisse pas etre rejouee un mois plus tard.
///
///   **Ne pas bloquer.** L'envoi ne doit jamais retarder la reponse
///   faite au candidat. Un serveur d'en face qui met trente secondes a
///   repondre ne doit pas faire attendre trente secondes celui qui
///   vient de postuler.
///
///   **Abandonner.** Une URL morte frappee toutes les minutes pendant
///   six mois use nos files et le serveur d'en face. Au-dela de dix
///   echecs consecutifs, l'abonnement se desactive tout seul et son
///   proprietaire en est informe.
/// </summary>
public class WebhookService
{
    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<WebhookService> _journal;

    /// <summary>Au-dela, on cesse. Dix echecs de suite ne sont pas un incident passager.</summary>
    private const int EchecsAvantDesactivation = 10;

    public WebhookService(AppDbContext context, IHttpClientFactory clients, ILogger<WebhookService> journal)
    {
        _context = context;
        _clients = clients;
        _journal = journal;
    }

    /// <summary>Les evenements auxquels on peut s'abonner.</summary>
    public static readonly Dictionary<string, string> Evenements = new()
    {
        ["candidature.creee"] = "Une candidature a ete deposee sur une de vos offres",
        ["candidature.statut"] = "Le statut d'une candidature a change",
        ["offre.publiee"] = "Une de vos offres a ete publiee",
        ["offre.fermee"] = "Une de vos offres a ete fermee ou a expire",
        ["entretien.planifie"] = "Un entretien a ete planifie",
        ["message.recu"] = "Un message a ete recu dans la messagerie",
    };

    /// <summary>
    /// Diffuse un evenement aux abonnes d'un recruteur.
    ///
    /// Enregistre les livraisons puis rend la main : l'envoi effectif
    /// part en tache detachee. La reponse au candidat ne doit pas
    /// attendre le serveur d'un tiers.
    /// </summary>
    public async Task Diffuser(string userId, string evenement, object charge)
    {
        var abonnes = await _context.Webhooks
            .Where(w => w.UserId == userId && w.Actif)
            .ToListAsync();

        // Le filtrage se fait ici et non en base : « Evenements » est
        // une liste separee par des virgules, et « LIKE '%offre.publiee%' »
        // attraperait aussi « offre.publiee.brouillon » si elle existait
        // un jour.
        abonnes = abonnes
            .Where(w => w.Evenements.Split(',', StringSplitOptions.TrimEntries).Contains(evenement))
            .ToList();

        if (abonnes.Count == 0) return;

        var corps = JsonSerializer.Serialize(new
        {
            evenement,
            horodatage = DateTime.UtcNow,
            donnees = charge,
        });

        foreach (var abonne in abonnes)
        {
            var livraison = new LivraisonWebhook
            {
                WebhookId = abonne.Id,
                Evenement = evenement,
                Charge = corps.Length > 8_000 ? corps[..8_000] : corps,
            };
            _context.LivraisonsWebhook.Add(livraison);
        }

        await _context.SaveChangesAsync();

        foreach (var abonne in abonnes)
            _ = Livrer(abonne.Id, abonne.Url, abonne.Secret, corps);
    }

    /// <summary>
    /// L'envoi lui-meme. Detache : toute exception qui en sortirait
    /// serait perdue, d'ou l'enrobage complet.
    /// </summary>
    private async Task Livrer(int webhookId, string url, string secret, string corps)
    {
        var horodatage = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = Signer(secret, horodatage, corps);

        int? code = null;
        string? erreur = null;

        try
        {
            var client = _clients.CreateClient();
            // Court volontairement : on notifie, on n'attend pas un
            // traitement. Un tiers qui a besoin de dix secondes doit
            // accuser reception puis travailler de son cote.
            client.Timeout = TimeSpan.FromSeconds(10);

            using var requete = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(corps, Encoding.UTF8, "application/json"),
            };
            requete.Headers.Add("X-Lpde-Horodatage", horodatage);
            requete.Headers.Add("X-Lpde-Signature", signature);

            var reponse = await client.SendAsync(requete);
            code = (int)reponse.StatusCode;
            if (!reponse.IsSuccessStatusCode) erreur = $"HTTP {code}";
        }
        catch (Exception ex)
        {
            erreur = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
        }

        // Une portee neuve : celle de la requete d'origine est fermee
        // depuis longtemps quand cette tache se termine.
        try
        {
            await Consigner(webhookId, code, erreur);
        }
        catch (Exception ex)
        {
            _journal.LogWarning(ex, "Consignation de livraison webhook impossible");
        }
    }

    private async Task Consigner(int webhookId, int? code, string? erreur)
    {
        var livraison = await _context.LivraisonsWebhook
            .Where(l => l.WebhookId == webhookId && l.LivreLe == null)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync();

        if (livraison is not null)
        {
            livraison.CodeReponse = code;
            livraison.Erreur = erreur;
            livraison.Tentatives++;
            livraison.LivreLe = DateTime.UtcNow;
        }

        var abonne = await _context.Webhooks.FindAsync(webhookId);
        if (abonne is not null)
        {
            abonne.DerniereLivraison = DateTime.UtcNow;
            abonne.DerniereErreur = erreur;

            if (erreur is null)
            {
                abonne.EchecsConsecutifs = 0;
            }
            else if (++abonne.EchecsConsecutifs >= EchecsAvantDesactivation)
            {
                abonne.Actif = false;
                _journal.LogWarning(
                    "Webhook {Id} desactive apres {Echecs} echecs consecutifs",
                    webhookId, abonne.EchecsConsecutifs);
            }
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// « t=<horodatage>,v1=<hmac> ». L'horodatage entre dans la
    /// signature : sans lui, une livraison capturee pourrait etre
    /// rejouee telle quelle des mois plus tard.
    /// </summary>
    public static string Signer(string secret, string horodatage, string corps)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var empreinte = hmac.ComputeHash(Encoding.UTF8.GetBytes(horodatage + "." + corps));
        return $"t={horodatage},v1={Convert.ToHexString(empreinte).ToLowerInvariant()}";
    }

    /// <summary>Assez long pour resister, assez court pour tenir sur une ligne.</summary>
    public static string NouveauSecret() =>
        "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
