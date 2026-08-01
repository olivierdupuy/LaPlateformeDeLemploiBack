using System.Text;
using System.Text.Json;

namespace lpdeBack.Services;

/// <summary>
/// L'expedition par Brevo.
///
/// La plateforme sait deja envoyer du courriel par SMTP — c'est ce qui
/// porte les mots de passe oublies et les alertes de connexion. Pourquoi
/// un second chemin ?
///
/// Parce que ce ne sont pas les memes messages. Un lien de
/// reinitialisation part a une personne qui vient de le demander : il est
/// attendu, il arrive. Une lettre d'information part a des milliers de
/// gens qui ne l'attendaient pas ce jour-la, et cela ne s'envoie pas
/// depuis une boite Gandi ordinaire sans y ruiner sa reputation
/// d'expediteur — au point que les mots de passe oublies cesseraient
/// d'arriver eux aussi.
///
/// Les deux canaux restent donc separes : le transactionnel chez Gandi,
/// le massif chez Brevo, qui gere pour nous la reputation, les retours en
/// erreur et les plaintes.
/// </summary>
public class BrevoService
{
    private const string Racine = "https://api.brevo.com/v3";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<BrevoService> _log;
    private readonly string? _cle;
    private readonly string _expediteur;
    private readonly string _nomExpediteur;
    private readonly string? _reponseVers;

    public BrevoService(IHttpClientFactory http, ILogger<BrevoService> log, IConfiguration config)
    {
        _http = http;
        _log = log;
        _cle = config["Brevo:ApiKey"];
        _expediteur = config["Brevo:From"] ?? config["Email:From"] ?? "noreply@laplateformedelemploi.com";
        _nomExpediteur = config["Brevo:FromName"] ?? "La Plateforme de l'emploi";
        _reponseVers = config["Brevo:ReplyTo"] ?? config["Email:ReplyTo"];
    }

    public bool EstConfigure => !string.IsNullOrWhiteSpace(_cle);

    public string Etat => EstConfigure
        ? $"Brevo, expediteur {_expediteur}" +
          (string.IsNullOrWhiteSpace(_reponseVers) ? "" : $", reponses vers {_reponseVers}")
        : "aucune cle Brevo configuree : les campagnes ne partent pas";

    public record Resultat(bool Parti, string? Identifiant, string? Erreur, bool DefinitiF = false);

    /// <summary>
    /// Les en-tetes de tout appel a Brevo.
    ///
    /// L'agent utilisateur n'est pas une politesse : Brevo est derriere
    /// Cloudflare, qui refuse par un 403 les clients sans agent reconnu —
    /// avec un message parlant de « browser signature » ou l'on cherche en
    /// vain une erreur d'authentification.
    /// </summary>
    private void Entetes(HttpRequestMessage r)
    {
        r.Headers.Add("api-key", _cle);
        r.Headers.Add("accept", "application/json");
        r.Headers.Add("User-Agent", "LaPlateformeDeLemploi/1.0 (+https://www.laplateformedelemploi.com)");
    }

    /// <summary>Ce que Brevo dit de notre compte, avant qu'on lui confie une campagne.</summary>
    public record Diagnostic(bool Joignable, string? Compte, int? CreditsRestants,
                             bool ExpediteurValide, List<string> ExpediteursActifs, string? Erreur);

    /// <summary>
    /// Interroge Brevo sur deux points qui font echouer une campagne
    /// entiere, et qu'on ne decouvre autrement qu'apres coup, dans les
    /// journaux :
    ///
    ///   - l'expediteur est-il valide chez eux ? Brevo refuse d'expedier au
    ///     nom d'une adresse qu'on n'a pas prouve posseder, et refuse alors
    ///     chaque message un par un ;
    ///   - reste-t-il assez de credits ? L'offre gratuite plafonne a trois
    ///     cents envois par jour, et une campagne plus large s'arrete au
    ///     milieu sans que personne ne l'ait demande.
    /// </summary>
    public async Task<Diagnostic> Interroger(CancellationToken ct = default)
    {
        if (!EstConfigure)
            return new Diagnostic(false, null, null, false, new(), "Aucune cle Brevo n'est configuree.");

        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            using var rc = new HttpRequestMessage(HttpMethod.Get, $"{Racine}/account");
            Entetes(rc);
            var compte = await client.SendAsync(rc, ct);
            if (!compte.IsSuccessStatusCode)
                return new Diagnostic(false, null, null, false, new(),
                    compte.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "Brevo a refuse la cle d'API."
                        : $"Brevo a repondu {(int)compte.StatusCode}.");

            using var dc = JsonDocument.Parse(await compte.Content.ReadAsStringAsync(ct));
            var nom = dc.RootElement.TryGetProperty("companyName", out var n) ? n.GetString() : null;
            int? credits = null;
            if (dc.RootElement.TryGetProperty("plan", out var plans) && plans.ValueKind == JsonValueKind.Array)
                foreach (var p in plans.EnumerateArray())
                    if (p.TryGetProperty("credits", out var c) && c.ValueKind == JsonValueKind.Number)
                        credits = c.GetInt32();

            using var rs = new HttpRequestMessage(HttpMethod.Get, $"{Racine}/senders");
            Entetes(rs);
            var envois = await client.SendAsync(rs, ct);
            var actifs = new List<string>();
            if (envois.IsSuccessStatusCode)
            {
                using var ds = JsonDocument.Parse(await envois.Content.ReadAsStringAsync(ct));
                if (ds.RootElement.TryGetProperty("senders", out var liste) && liste.ValueKind == JsonValueKind.Array)
                    foreach (var e in liste.EnumerateArray())
                        if (e.TryGetProperty("active", out var a) && a.GetBoolean()
                            && e.TryGetProperty("email", out var em))
                            actifs.Add(em.GetString() ?? "");
            }

            return new Diagnostic(true, nom, credits,
                                  actifs.Contains(_expediteur, StringComparer.OrdinalIgnoreCase),
                                  actifs, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Brevo injoignable pour le diagnostic");
            return new Diagnostic(false, null, null, false, new(), "Brevo est injoignable.");
        }
    }

    public string Expediteur => _expediteur;

    /// <summary>
    /// Expedie un message a une personne.
    ///
    /// Un envoi par destinataire, et non un lot : Brevo accepte des lots de
    /// mille, mais on perd alors le detail de qui a recu quoi. Une lettre
    /// d'information se juge a ses retours en erreur, et une erreur qu'on
    /// ne sait pas rattacher a une adresse ne sert a rien.
    /// </summary>
    public async Task<Resultat> Envoyer(string destinataire, string? nom, string sujet,
                                        string corpsHtml, string corpsTexte,
                                        string? lienDesinscription = null,
                                        CancellationToken ct = default)
    {
        if (!EstConfigure)
        {
            _log.LogWarning("Campagne non expediee (aucune cle Brevo).\n  A : {Destinataire}\n  Sujet : {Sujet}",
                            destinataire, sujet);
            return new Resultat(false, null, "Aucune cle Brevo n'est configuree.");
        }

        var corps = new Dictionary<string, object?>
        {
            ["sender"] = new { email = _expediteur, name = _nomExpediteur },
            ["to"] = new[] { string.IsNullOrWhiteSpace(nom)
                ? new Dictionary<string, object> { ["email"] = destinataire }
                : new Dictionary<string, object> { ["email"] = destinataire, ["name"] = nom } },
            ["subject"] = sujet,
            ["htmlContent"] = corpsHtml,
            ["textContent"] = corpsTexte,
        };

        if (!string.IsNullOrWhiteSpace(_reponseVers))
            corps["replyTo"] = new { email = _reponseVers };

        // ── L'en-tete que les messageries lisent ──
        // « List-Unsubscribe » place le bouton de desinscription dans
        // l'interface de Gmail et d'Outlook, a cote de l'expediteur. Sans
        // lui, quelqu'un qui ne veut plus rien recevoir clique sur
        // « signaler comme indesirable » — ce qui coute infiniment plus
        // cher a la reputation qu'une desinscription.
        if (!string.IsNullOrWhiteSpace(lienDesinscription))
        {
            corps["headers"] = new Dictionary<string, string>
            {
                ["List-Unsubscribe"] = $"<{lienDesinscription}>",
                ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
            };
        }

        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            using var requete = new HttpRequestMessage(HttpMethod.Post, $"{Racine}/smtp/email");
            Entetes(requete);
            requete.Content = new StringContent(JsonSerializer.Serialize(corps), Encoding.UTF8, "application/json");

            var reponse = await client.SendAsync(requete, ct);
            var texte = await reponse.Content.ReadAsStringAsync(ct);

            if (!reponse.IsSuccessStatusCode)
            {
                var (message, definitif) = LireErreur(texte, (int)reponse.StatusCode);
                _log.LogWarning("Brevo a refuse l'envoi a {Destinataire} : {Code} — {Detail}",
                                destinataire, (int)reponse.StatusCode, Tronquer(texte, 200));
                return new Resultat(false, null, message, definitif);
            }

            string? id = null;
            try
            {
                using var doc = JsonDocument.Parse(texte);
                if (doc.RootElement.TryGetProperty("messageId", out var m)) id = m.GetString();
            }
            catch { /* accepte sans identifiant lisible : l'envoi a reussi quand meme */ }

            return new Resultat(true, id, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Echec de l'envoi Brevo a {Destinataire}", destinataire);
            return new Resultat(false, null, "L'envoi a echoue.");
        }
    }

    /// <summary>
    /// Distingue l'echec passager de l'echec definitif.
    ///
    /// Reessayer sur une adresse qui n'existe pas est une perte de temps et
    /// une atteinte a la reputation ; reessayer apres un depassement de
    /// quota est au contraire la bonne conduite. L'appelant doit pouvoir
    /// faire la difference.
    /// </summary>
    private static (string Message, bool Definitif) LireErreur(string corps, int code)
    {
        var texte = corps;
        try
        {
            using var doc = JsonDocument.Parse(corps);
            if (doc.RootElement.TryGetProperty("message", out var m))
                texte = m.GetString() ?? corps;
        }
        catch { /* corps illisible */ }

        return code switch
        {
            401 => ("Brevo a refuse la cle d'API.", true),
            400 when texte.Contains("email", StringComparison.OrdinalIgnoreCase)
                  && texte.Contains("valid", StringComparison.OrdinalIgnoreCase)
                  => ("Adresse refusee par Brevo.", true),
            402 => ("Le credit Brevo est epuise.", false),
            429 => ("Limite de debit atteinte chez Brevo.", false),
            _ when code >= 500 => ("Brevo est momentanement indisponible.", false),
            _ => (Tronquer(texte, 180), code == 400),
        };
    }

    private static string Tronquer(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max] + "…";
}
