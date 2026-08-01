using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Ce que la lettre d'information sait faire : recruter ses abonnes,
/// choisir a qui elle parle, et personnaliser ce qu'elle dit.
///
/// L'expedition proprement dite n'est pas ici : elle vit dans un service
/// de fond, parce qu'ecrire a trois mille personnes ne peut pas se faire
/// dans le temps d'une requete HTTP.
/// </summary>
public class NewsletterService
{
    private readonly AppDbContext _context;
    private readonly IEmailSender _mail;
    private readonly IConfiguration _config;
    private readonly ILogger<NewsletterService> _log;

    public NewsletterService(AppDbContext context, IEmailSender mail,
                             IConfiguration config, ILogger<NewsletterService> log)
    {
        _context = context;
        _mail = mail;
        _config = config;
        _log = log;
    }

    private string Site => (_config["App:PublicUrl"] ?? "").TrimEnd('/');

    public string LienDesinscription(NewsletterSubscriber a) =>
        $"{Site}/newsletter/desinscription?jeton={Uri.EscapeDataString(a.UnsubscribeToken)}";

    // ══════════════════════════════════════
    //  1. S'ABONNER
    // ══════════════════════════════════════

    public record Issue(bool Ok, string Message, bool DejaAbonne = false);

    /// <summary>
    /// Enregistre une demande d'abonnement et envoie le lien de
    /// confirmation.
    ///
    /// Double opt-in : rien n'est actif tant que la personne n'a pas
    /// clique. Cela evite d'ecrire a quelqu'un dont un tiers aurait saisi
    /// l'adresse, et c'est la preuve de consentement la plus solide devant
    /// la CNIL.
    ///
    /// La reponse est la meme que l'adresse soit nouvelle, deja en attente
    /// ou deja confirmee : dire « vous etes deja abonne » revelerait a un
    /// inconnu qu'une adresse figure dans nos listes.
    /// </summary>
    public async Task<Issue> Abonner(string email, string? prenom, string? nom, string source,
                                     string? ip, string? userId = null,
                                     string? categories = null, string? ville = null,
                                     CancellationToken ct = default)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        if (!EstUneAdresse(email))
            return new Issue(false, "Cette adresse ne semble pas valide.");

        var abonne = await _context.NewsletterSubscribers.FirstOrDefaultAsync(s => s.Email == email, ct);
        var neuf = abonne == null;

        if (neuf)
        {
            abonne = new NewsletterSubscriber
            {
                Email = email,
                UnsubscribeToken = Jeton(),
                Source = source,
                ConsentIp = ip,
                CreatedAt = DateTime.UtcNow,
            };
            _context.NewsletterSubscribers.Add(abonne);
        }

        // Un abonne qui revient apres s'etre desinscrit redevient un abonne
        // en attente : son geste d'aujourd'hui compte, pas son refus d'hier.
        if (abonne!.Status == "Unsubscribed")
        {
            abonne.Status = "Pending";
            abonne.UnsubscribedAt = null;
            abonne.UnsubscribeReason = null;
            abonne.ConsecutiveFailures = 0;
        }

        if (!string.IsNullOrWhiteSpace(prenom)) abonne.FirstName = prenom.Trim();
        if (!string.IsNullOrWhiteSpace(nom)) abonne.LastName = nom.Trim();
        if (!string.IsNullOrWhiteSpace(categories)) abonne.Categories = categories;
        if (!string.IsNullOrWhiteSpace(ville))
        {
            abonne.City = ville.Trim();
            abonne.Department = Departement(ville);
        }
        if (userId != null) abonne.UserId = userId;
        abonne.ConsentAt = DateTime.UtcNow;
        if (ip != null) abonne.ConsentIp = ip;

        if (abonne.Status == "Confirmed")
        {
            await _context.SaveChangesAsync(ct);
            return new Issue(true, MessageNeutre, DejaAbonne: true);
        }

        // Un lien de confirmation renvoye toutes les dix secondes servirait
        // a inonder une boite. Un par quart d'heure suffit.
        var recent = abonne.ConfirmTokenSentAt is { } envoye
                     && DateTime.UtcNow - envoye < TimeSpan.FromMinutes(15);
        if (!recent)
        {
            abonne.ConfirmToken = Jeton();
            abonne.ConfirmTokenSentAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);

        if (!recent)
        {
            var lien = $"{Site}/newsletter/confirmer?jeton={Uri.EscapeDataString(abonne.ConfirmToken!)}";
            await _mail.Envoyer(ModelesCourriel.ConfirmationNewsletter(
                abonne.Email, abonne.FirstName, lien, LienDesinscription(abonne)), ct);
        }

        return new Issue(true, MessageNeutre);
    }

    private const string MessageNeutre =
        "Presque fini : ouvrez le message que nous venons d'envoyer et cliquez sur le lien de confirmation. " +
        "Sans ce clic, vous ne recevrez rien — c'est ce qui garantit que personne ne peut vous abonner a votre place.";

    /// <summary>Le clic sur le lien recu. Le jeton ne resservira pas.</summary>
    public async Task<Issue> Confirmer(string jeton, CancellationToken ct = default)
    {
        var abonne = await _context.NewsletterSubscribers
            .FirstOrDefaultAsync(s => s.ConfirmToken == jeton, ct);

        if (abonne == null)
            return new Issue(false, "Ce lien n'est plus valable. Il a peut-etre deja servi : reinscrivez-vous si vous ne recevez rien.");

        abonne.Status = "Confirmed";
        abonne.ConfirmedAt = DateTime.UtcNow;
        abonne.ConfirmToken = null;   // un lien qui ne sert qu'une fois
        await _context.SaveChangesAsync(ct);

        return new Issue(true, "Votre abonnement est confirme. Vous pouvez vous desabonner a tout moment, depuis le bas de chaque message.");
    }

    /// <summary>
    /// La desinscription. Aucun compte, aucun mot de passe, aucune
    /// confirmation : un clic suffit, et c'est la loi. On note le motif
    /// quand il est donne, sans jamais l'exiger.
    /// </summary>
    public async Task<Issue> Desabonner(string jeton, string? motif, CancellationToken ct = default)
    {
        var abonne = await _context.NewsletterSubscribers
            .FirstOrDefaultAsync(s => s.UnsubscribeToken == jeton, ct);

        if (abonne == null)
            return new Issue(false, "Ce lien de desinscription n'est pas reconnu.");

        if (abonne.Status == "Unsubscribed")
            return new Issue(true, "Vous etiez deja desabonne. Vous ne recevez plus rien de notre part.");

        abonne.Status = "Unsubscribed";
        abonne.UnsubscribedAt = DateTime.UtcNow;
        abonne.UnsubscribeReason = string.IsNullOrWhiteSpace(motif) ? null : motif.Trim()[..Math.Min(200, motif.Trim().Length)];
        await _context.SaveChangesAsync(ct);

        return new Issue(true, "C'est fait : vous ne recevrez plus notre lettre d'information.");
    }

    // ══════════════════════════════════════
    //  2. QUI RECEVRA
    // ══════════════════════════════════════

    /// <summary>
    /// Les destinataires d'une campagne.
    ///
    /// Un abonne confirme, non desinscrit, et dont les trois derniers
    /// envois n'ont pas tous echoue : ecrire encore a une adresse qui
    /// rebondit abime la reputation de tout le domaine, et finit par
    /// empecher les mots de passe oublies d'arriver.
    /// </summary>
    public IQueryable<NewsletterSubscriber> Destinataires(NewsletterCampaign c)
    {
        var q = _context.NewsletterSubscribers
            .Where(s => s.Status == "Confirmed" && s.UnsubscribedAt == null && s.ConsecutiveFailures < 3);

        var roles = Decouper(c.SegmentRoles);
        if (roles.Count > 0)
        {
            // « Guest » designe un abonne sans compte : il n'a pas de role,
            // c'est precisement ce qui le definit.
            var veutInvites = roles.Contains("Guest");
            var rolesCompte = roles.Where(r => r != "Guest").ToList();
            q = q.Where(s =>
                (veutInvites && s.UserId == null) ||
                (s.UserId != null && s.User != null && rolesCompte.Contains(s.User.Role)));
        }

        foreach (var cat in Decouper(c.SegmentCategories))
            q = q.Where(s => s.Categories != null && s.Categories.Contains(cat));

        var villes = Decouper(c.SegmentCities);
        if (villes.Count > 0)
            q = q.Where(s => s.City != null && villes.Any(v => s.City.Contains(v)));

        var deps = Decouper(c.SegmentDepartments);
        if (deps.Count > 0)
            q = q.Where(s => s.Department != null && deps.Contains(s.Department));

        var maintenant = DateTime.UtcNow;
        q = c.SegmentActivity switch
        {
            "Recents" => q.Where(s => s.CreatedAt >= maintenant.AddDays(-30)),
            // Dormant se juge sur le compte quand il y en a un ; un abonne
            // sans compte ne se connecte jamais, il n'est donc pas dormant.
            "Dormants" => q.Where(s => s.UserId != null && s.User != null
                                       && (s.User.LastLoginAt == null || s.User.LastLoginAt < maintenant.AddDays(-90))),
            _ => q,
        };

        return q;
    }

    public Task<int> CompterDestinataires(NewsletterCampaign c, CancellationToken ct = default)
        => Destinataires(c).CountAsync(ct);

    // ══════════════════════════════════════
    //  3. PERSONNALISER
    // ══════════════════════════════════════

    /// <summary>Les champs de fusion offerts a la redaction.</summary>
    public static readonly (string Cle, string Description)[] Champs =
    {
        ("prenom", "Le prenom, ou « Bonjour » reste seul si on ne le connait pas"),
        ("nom", "Le nom de famille, vide s'il est inconnu"),
        ("email", "L'adresse du destinataire"),
        ("ville", "La ville declaree"),
        ("lien_desinscription", "L'adresse de desinscription — ajoutee d'office si vous l'omettez"),
    };

    /// <summary>
    /// Remplace les champs de fusion.
    ///
    /// Le rendu se fait chez nous et non chez Brevo : ce qu'on voit dans
    /// l'apercu est alors exactement ce qui partira, et changer de
    /// fournisseur un jour ne demandera pas de reecrire toutes les
    /// campagnes.
    ///
    /// Les valeurs sont echappees : un abonne dont le nom contient une
    /// apostrophe ou un chevron ne doit pas pouvoir casser la mise en page
    /// — ni y glisser du balisage.
    /// </summary>
    public string Rendre(string gabarit, NewsletterSubscriber a, bool html = true)
    {
        string E(string? v) => html ? WebUtility.HtmlEncode(v ?? "") : (v ?? "");

        var valeurs = new Dictionary<string, string>
        {
            ["prenom"] = E(a.FirstName),
            ["nom"] = E(a.LastName),
            ["email"] = E(a.Email),
            ["ville"] = E(a.City),
            ["lien_desinscription"] = LienDesinscription(a),
        };

        return Regex.Replace(gabarit, @"\{\{\s*([a-z_]+)\s*\}\}",
            m => valeurs.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    /// <summary>
    /// Le corps complet d'un message, enveloppe et pied compris.
    ///
    /// Le lien de desinscription est ajoute d'office si la redaction ne
    /// l'a pas place elle-meme : l'oublier rendrait l'envoi illegal, et ce
    /// n'est pas une erreur qu'on peut laisser dependre de l'attention de
    /// celui qui redige a onze heures du soir.
    /// </summary>
    public (string Html, string Texte) Composer(NewsletterCampaign c, NewsletterSubscriber a)
    {
        var corps = Rendre(c.BodyHtml, a);
        var lien = LienDesinscription(a);
        var apercu = string.IsNullOrWhiteSpace(c.PreviewText) ? "" : Rendre(c.PreviewText, a);

        var pied = corps.Contains(lien, StringComparison.OrdinalIgnoreCase)
            ? ""
            : $"""
               <p style="margin:0">Vous recevez ce message parce que vous vous etes abonne a la lettre
               d'information de La Plateforme de l'emploi.</p>
               <p style="margin:6px 0 0"><a href="{lien}" style="color:#577177">Se desabonner en un clic</a></p>
               """;

        // L'objet doit etre rendu avant d'entrer dans l'enveloppe : il y sert
        // de <title>, et un « Bonjour {{prenom}} » brut s'y afficherait tel
        // quel dans les webmails qui montrent ce titre en onglet.
        var objet = Rendre(c.Subject, a, html: false);
        var html = ModelesCourriel.EnveloppeNewsletter(objet, apercu, corps, pied);
        var texte = HtmlEnTexte(corps) + $"\n\n—\nSe desabonner : {lien}\n";
        return (html, texte);
    }

    // ══════════════════════════════════════
    //  Aides
    // ══════════════════════════════════════

    private static string Jeton() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    /// <summary>
    /// Une adresse acceptable.
    ///
    /// La regle precedente n'excluait que l'arobase et les espaces. Elle
    /// laissait donc entrer « &lt;img/src=x/onerror=…&gt;@evil.fr » : une
    /// adresse au regard de l'expression, une charge active des qu'un
    /// ecran la reaffiche. Le formulaire d'abonnement etant ouvert a
    /// n'importe qui, cela donnait a un inconnu le moyen de faire executer
    /// du code dans la console d'un administrateur — le compte le mieux
    /// protege du site, et le plus interessant a prendre.
    ///
    /// On s'en tient donc aux caracteres qu'une adresse porte reellement.
    /// Aucune adresse legitime n'est perdue : les chevrons, guillemets et
    /// apostrophes n'y figurent que dans des formes que ni Brevo ni Gandi
    /// n'accepteraient de toute facon.
    ///
    /// Cela ne dispense pas d'echapper a l'affichage — une base peut avoir
    /// ete remplie avant cette regle, et une seule barriere n'en est pas
    /// une.
    /// </summary>
    public static bool EstUneAdresse(string? e) =>
        !string.IsNullOrWhiteSpace(e)
        && e.Length <= 254
        && Regex.IsMatch(e, @"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~.\-]+@[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}$")
        && !e.Contains("..");

    /// <summary>« 34 - Montpellier » rend « 34 ». La Corse compte double : 2A, 2B.</summary>
    public static string? Departement(string? ville)
    {
        if (string.IsNullOrWhiteSpace(ville)) return null;
        var m = Regex.Match(ville.Trim(), @"^\s*(\d{2,3}|2[AB])\b", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static List<string> Decouper(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Une version texte lisible, pour les messageries qui n'affichent pas le HTML.</summary>
    private static string HtmlEnTexte(string html)
    {
        var t = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"</(p|div|h[1-6]|li|tr)>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<li[^>]*>", "- ", RegexOptions.IgnoreCase);
        // Un lien perd son adresse en devenant du texte : on la garde.
        t = Regex.Replace(t, @"<a[^>]+href=""([^""]+)""[^>]*>(.*?)</a>", "$2 ($1)",
                          RegexOptions.IgnoreCase | RegexOptions.Singleline);
        t = Regex.Replace(t, @"<[^>]+>", "");
        t = WebUtility.HtmlDecode(t);
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        return t.Trim();
    }
}
