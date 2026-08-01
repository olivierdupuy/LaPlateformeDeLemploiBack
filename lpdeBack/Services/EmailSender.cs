using System.Net;
using System.Net.Mail;

namespace lpdeBack.Services;

/// <summary>Un message pret a partir.</summary>
public record Courriel(string Destinataire, string Sujet, string CorpsHtml, string CorpsTexte);

/// <summary>
/// L'expedition de courriel.
///
/// La plateforme n'en avait aucune : ni serveur, ni gabarit, ni paquet.
/// Les alertes de recherche enregistree se disaient « actives » sans que
/// rien ne parte, et un mot de passe oublie ne se recuperait qu'en
/// derangeant un administrateur.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Un serveur est-il configure ? Quand il ne l'est pas, les messages
    /// sont ecrits au journal plutot que perdus en silence : on voit en
    /// developpement ce qu'aurait recu l'utilisateur, lien compris.
    /// </summary>
    bool EstConfigure { get; }

    /// <summary>De quoi rendre compte a l'administration, sans secret.</summary>
    string Etat { get; }

    Task<bool> Envoyer(Courriel message, CancellationToken ct = default);
}

public sealed class EmailSender : IEmailSender
{
    private readonly ILogger<EmailSender> _log;
    private readonly string? _hote;
    private readonly int _port;
    private readonly string? _identifiant;
    private readonly string? _motDePasse;
    private readonly string _expediteur;
    private readonly string _nomExpediteur;
    private readonly bool _ssl;

    public EmailSender(IConfiguration config, ILogger<EmailSender> log)
    {
        _log = log;
        _hote = config["Email:Host"];
        _port = int.TryParse(config["Email:Port"], out var p) ? p : 587;
        _identifiant = config["Email:User"];
        _motDePasse = config["Email:Password"];
        _expediteur = config["Email:From"] ?? "no-reply@laplateformedelemploi.com";
        _nomExpediteur = config["Email:FromName"] ?? "La Plateforme de l'emploi";
        _ssl = !bool.TryParse(config["Email:Ssl"], out var s) || s;
    }

    /// <summary>
    /// Le serveur et l'identifiant, pas seulement le serveur.
    ///
    /// L'hote figure dans appsettings — ce n'est pas un secret, et il ne
    /// change pas. Seuls l'identifiant et le mot de passe viennent des
    /// secrets. Se contenter de l'hote rendrait donc l'expedition
    /// « configuree » sur toute machine de developpement : les messages
    /// partiraient vers un serveur qui les refuse, au lieu d'etre ecrits
    /// au journal ou l'on peut suivre un lien de reinitialisation.
    /// </summary>
    public bool EstConfigure =>
        !string.IsNullOrWhiteSpace(_hote) && !string.IsNullOrWhiteSpace(_identifiant);

    public string Etat => EstConfigure
        ? $"{_hote}:{_port} ({(_ssl ? "STARTTLS" : "en clair")}), compte {_identifiant}, expediteur {_expediteur}"
        : string.IsNullOrWhiteSpace(_hote)
            ? "aucun serveur configuré : les messages sont écrits au journal"
            : $"{_hote} est renseigné mais aucun compte ne l'est : les messages sont écrits au journal";

    public async Task<bool> Envoyer(Courriel message, CancellationToken ct = default)
    {
        // Sans serveur, le message va au journal. Ecrire le corps en texte
        // brut suffit a suivre un flux complet en developpement : le lien
        // de reinitialisation s'y lit et se colle dans un navigateur.
        if (!EstConfigure)
        {
            _log.LogWarning(
                "Courriel non expedie (aucun serveur configure).\n  A : {Destinataire}\n  Sujet : {Sujet}\n{Corps}",
                message.Destinataire, message.Sujet, message.CorpsTexte);
            return false;
        }

        try
        {
            using var client = new SmtpClient(_hote, _port)
            {
                EnableSsl = _ssl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20_000,
            };

            // UseDefaultCredentials doit etre remis a faux avant d'assigner
            // Credentials : dans l'ordre inverse, il ecrase ce qu'on vient
            // de poser et le serveur repond « authentification requise »
            // sans qu'on comprenne pourquoi.
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(_identifiant, _motDePasse);

            using var mail = new MailMessage
            {
                From = new MailAddress(_expediteur, _nomExpediteur),
                Subject = message.Sujet,
                Body = message.CorpsHtml,
                IsBodyHtml = true,
            };
            mail.To.Add(message.Destinataire);

            // Le texte brut n'est pas une politesse : les filtres anti-spam
            // penalisent un message qui n'a que du HTML, et certains clients
            // n'affichent que celui-la.
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.CorpsTexte, null, "text/plain"));
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.CorpsHtml, null, "text/html"));

            await client.SendMailAsync(mail, ct);
            _log.LogInformation("Courriel expedie a {Destinataire} : {Sujet}", message.Destinataire, message.Sujet);
            return true;
        }
        catch (Exception ex)
        {
            // Un envoi qui echoue ne doit jamais faire echouer l'action qui
            // l'a declenche : on n'annule pas une inscription parce que le
            // serveur de courriel bougonne.
            _log.LogError(ex, "Echec de l'envoi a {Destinataire} : {Sujet}", message.Destinataire, message.Sujet);
            return false;
        }
    }
}
