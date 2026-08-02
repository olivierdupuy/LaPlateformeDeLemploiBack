using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// L'expedition des campagnes.
///
/// Ecrire a trois mille personnes prend des minutes : cela ne peut pas se
/// faire dans le temps d'une requete HTTP, ni dans un Task.Run qu'un
/// redemarrage d'IIS emporterait au milieu. Un service de fond regarde
/// donc toutes les vingt secondes s'il y a une campagne a servir, et la
/// sert par petits paquets.
///
/// La reprise est le point delicat. Une ligne de livraison est ecrite
/// pour chaque destinataire AVANT le premier envoi, en « Pending ». Le
/// service ne traite que ces lignes-la, et les passe une par une a
/// « Sent » ou « Failed ». Un arret en cours de route ne perd donc rien
/// et ne redouble rien : au redemarrage, il reste exactement les lignes
/// qui n'ont pas encore ete servies.
/// </summary>
public class NewsletterSenderService : BackgroundService
{
    /// <summary>
    /// Par paquet. Brevo tolere davantage, mais un paquet court rend la
    /// main plus souvent : une campagne annulee s'arrete alors en quelques
    /// secondes, pas a la fin du lot.
    /// </summary>
    private const int Paquet = 25;

    /// <summary>
    /// Entre deux messages. Brevo limite le debit, et depasser cette limite
    /// fait echouer des envois qu'il aurait fallu simplement attendre.
    /// </summary>
    private static readonly TimeSpan Souffle = TimeSpan.FromMilliseconds(120);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NewsletterSenderService> _log;

    public NewsletterSenderService(IServiceScopeFactory scopes, ILogger<NewsletterSenderService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(25), ct); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            do
            {
                // La boucle tourne toutes les vingt secondes et ne fait
                // rien la plupart du temps ; c'est justement ce qu'on
                // veut savoir — qu'elle tourne encore. Une file qui
                // s'arrete ne se voit autrement qu'aux campagnes qui ne
                // partent pas, et on l'apprend par le destinataire.
                try
                {
                    await ServirUneCampagne(ct);
                    EtatDesServices.Noter("envoi-newsletter", true, "file relevée");
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Expedition de campagne en echec");
                    EtatDesServices.Noter("envoi-newsletter", false, e.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { /* arret normal */ }
    }

    private async Task ServirUneCampagne(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var brevo = scope.ServiceProvider.GetRequiredService<BrevoService>();
        var lettre = scope.ServiceProvider.GetRequiredService<NewsletterService>();
        var consentement = scope.ServiceProvider.GetRequiredService<ConsentementCourriel>();

        var campagne = await db.NewsletterCampaigns
            .Where(c => c.Status == "Sending")
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (campagne == null) return;

        // Sans cle, on ne vide pas la file en marquant tout en echec : la
        // campagne attend qu'on configure Brevo. Une campagne perdue faute
        // de configuration serait a reecrire entierement.
        if (!brevo.EstConfigure)
        {
            _log.LogWarning("Campagne {Id} en attente : aucune cle Brevo n'est configuree.", campagne.Id);
            return;
        }

        var aServir = await db.NewsletterDeliveries
            .Include(d => d.Subscriber)
            .Where(d => d.CampaignId == campagne.Id && d.Status == "Pending")
            .OrderBy(d => d.Id)
            .Take(Paquet)
            .ToListAsync(ct);

        if (aServir.Count == 0)
        {
            await Clore(db, campagne, ct);
            return;
        }

        // ── Les offres, une fois pour le paquet ──
        //
        // Un bloc « choisies » ou « recherche » sert la meme chose a tout
        // le monde : le resoudre par destinataire multiplierait la meme
        // requete par vingt-cinq, puis par trois mille sur la campagne
        // entiere. Seul le mode « pour chaque abonne » varie, et il se
        // calcule en memoire sur le vivier charge ici.
        //
        // Prepare a chaque paquet et non une fois pour toute la campagne :
        // un envoi de trois mille messages dure des heures, et une offre
        // pourvue entre-temps ne doit pas continuer de partir.
        var blocs = scope.ServiceProvider.GetRequiredService<LettreEnBlocs>();
        var offres = string.IsNullOrWhiteSpace(campagne.Blocs)
            ? ContexteOffres.Vide
            : await blocs.Preparer(LettreEnBlocs.Lire(campagne.Blocs), ct);

        foreach (var livraison in aServir)
        {
            if (ct.IsCancellationRequested) return;

            var abonne = livraison.Subscriber;

            // Quelqu'un a pu se desabonner depuis la preparation de la
            // campagne. Son geste prime sur notre file d'attente.
            if (abonne == null || !abonne.EstJoignable)
            {
                livraison.Status = "Failed";
                livraison.Error = "Desabonne avant l'envoi";
                continue;
            }

            // ── Le centre de preferences, et les adresses mortes ──
            //
            // Deux registres se sont mis a coexister : le desabonnement
            // historique ci-dessus, et les preferences par categorie.
            // Deux registres qui se contredisent finissent toujours par
            // ecrire a quelqu'un qui a dit non ; on interroge donc les
            // deux, et le refus l'emporte quel qu'en soit le registre.
            //
            // Le meme appel ecarte les adresses qui ne repondent plus.
            // Continuer a servir une boite fermee coute la reputation du
            // domaine, et cette reputation abimee fait tomber en
            // indesirable les mots de passe oublies.
            if (!await consentement.Autorise(abonne.Email, "lettre"))
            {
                livraison.Status = "Failed";
                livraison.Error = "Refuse dans les preferences, ou adresse bloquee";
                continue;
            }

            var (html, texte) = lettre.Composer(campagne, abonne, offres);
            var sujet = lettre.Rendre(campagne.Subject, abonne, html: false);
            var nom = $"{abonne.FirstName} {abonne.LastName}".Trim();

            var r = await brevo.Envoyer(abonne.Email, string.IsNullOrWhiteSpace(nom) ? null : nom,
                                        sujet, html, texte, lettre.LienDesinscription(abonne), ct);

            if (r.Parti)
            {
                livraison.Status = "Sent";
                livraison.SentAt = DateTime.UtcNow;
                livraison.ProviderMessageId = r.Identifiant;
                abonne.LastSentAt = DateTime.UtcNow;
                abonne.ConsecutiveFailures = 0;
            }
            else if (r.DefinitiF)
            {
                // Adresse refusee : inutile d'y revenir. Trois refus de
                // suite et l'abonne cesse d'etre servi — continuer a ecrire
                // a des adresses mortes abime la reputation du domaine, et
                // finit par empecher les mots de passe oublies d'arriver.
                livraison.Status = "Failed";
                livraison.Error = r.Erreur;
                abonne.ConsecutiveFailures++;
                if (abonne.ConsecutiveFailures >= 3) abonne.Status = "Bounced";
            }
            else
            {
                // Echec passager : la ligne reste « Pending » et sera
                // reprise au prochain tour.
                livraison.Error = r.Erreur;
                _log.LogWarning("Campagne {Id} : envoi differe a {Email} — {Erreur}",
                                campagne.Id, abonne.Email, r.Erreur);
                await db.SaveChangesAsync(ct);
                return;
            }

            await Task.Delay(Souffle, ct);
        }

        campagne.Delivered = await db.NewsletterDeliveries
            .CountAsync(d => d.CampaignId == campagne.Id && d.Status == "Sent", ct);
        campagne.Failed = await db.NewsletterDeliveries
            .CountAsync(d => d.CampaignId == campagne.Id && d.Status == "Failed", ct);

        await db.SaveChangesAsync(ct);

        var restants = await db.NewsletterDeliveries
            .CountAsync(d => d.CampaignId == campagne.Id && d.Status == "Pending", ct);
        if (restants == 0) await Clore(db, campagne, ct);
    }

    private async Task Clore(AppDbContext db, NewsletterCampaign campagne, CancellationToken ct)
    {
        campagne.Delivered = await db.NewsletterDeliveries
            .CountAsync(d => d.CampaignId == campagne.Id && d.Status == "Sent", ct);
        campagne.Failed = await db.NewsletterDeliveries
            .CountAsync(d => d.CampaignId == campagne.Id && d.Status == "Failed", ct);
        campagne.Status = "Sent";
        campagne.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _log.LogInformation("Campagne {Id} « {Sujet} » terminee : {Livres} remis, {Echecs} en echec.",
                            campagne.Id, campagne.Subject, campagne.Delivered, campagne.Failed);
    }
}
