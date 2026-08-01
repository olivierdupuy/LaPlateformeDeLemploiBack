using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Tenir les durees de conservation que la plateforme annonce.
///
/// Les mentions legales promettent « 2 ans apres la derniere
/// connexion » pour un compte, « 2 ans apres le dernier contact » pour
/// une candidature, « 12 mois » pour le journal. Rien ne les appliquait :
/// tout s'accumulait sans fin, et la promesse etait une phrase.
///
/// ── Pourquoi ce service ne detruit rien par defaut ──
///
/// Mis en service, il effacerait des comptes des la premiere nuit. Une
/// suppression de masse decidee par une machine merite d'etre vue avant
/// d'etre subie : tant que « purge_active » vaut false, le service
/// compte et journalise ce qu'il effacerait, sans y toucher. On lit les
/// chiffres, puis on l'autorise. C'est le meme principe que les
/// suppressions de masse du panneau, qui annoncent leur portee.
///
/// ── Prevenir avant d'effacer ──
///
/// Un compte inactif recoit un message deux mois avant sa fermeture.
/// Une seule connexion remet le compteur a zero. Effacer sans prevenir
/// serait defendable en droit et brutal en pratique.
/// </summary>
public class PurgeService : BackgroundService
{
    /// <summary>Une fois par jour suffit : ces durees se comptent en mois.</summary>
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(24);

    /// <summary>Combien de comptes on previent d'un coup, au plus.</summary>
    private const int PreavisParPassage = 200;

    private readonly IServiceScopeFactory _fabrique;
    private readonly ILogger<PurgeService> _log;

    public PurgeService(IServiceScopeFactory fabrique, ILogger<PurgeService> log)
    {
        _fabrique = fabrique;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(60), arret); }
        catch (OperationCanceledException) { return; }

        using var minuterie = new PeriodicTimer(Intervalle);
        do
        {
            try
            {
                await Passer(arret);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                EtatDesServices.Noter("purge", false, ex.Message);
                _log.LogError(ex, "Purge des donnees : passage interrompu");
            }
        }
        while (await minuterie.WaitForNextTickAsync(arret));
    }

    private async Task Passer(CancellationToken arret)
    {
        using var portee = _fabrique.CreateScope();
        var bd = portee.ServiceProvider.GetRequiredService<AppDbContext>();
        var comptes = portee.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var depot = portee.ServiceProvider.GetRequiredService<DepotFichiers>();
        var mail = portee.ServiceProvider.GetRequiredService<IEmailSender>();
        var journal = portee.ServiceProvider.GetRequiredService<ActivityLogService>();

        var reglages = await bd.PlatformSettings.ToDictionaryAsync(s => s.Key, s => s.Value, arret);

        int Nombre(string cle, int defaut)
            => reglages.TryGetValue(cle, out var v) && int.TryParse(v, out var n) && n > 0 ? n : defaut;

        var agit = reglages.TryGetValue("purge_active", out var a)
                   && a.Equals("true", StringComparison.OrdinalIgnoreCase);

        var moisCompte = Nombre("purge_compte_mois", 24);
        var joursPreavis = Nombre("purge_preavis_jours", 60);
        var moisCandidature = Nombre("purge_candidatures_mois", 24);
        var moisJournal = Nombre("purge_journal_mois", 12);

        var maintenant = DateTime.UtcNow;
        var seuilCompte = maintenant.AddMonths(-moisCompte);
        var seuilPreavis = seuilCompte.AddDays(joursPreavis);
        var seuilCandidature = maintenant.AddMonths(-moisCandidature);
        var seuilJournal = maintenant.AddMonths(-moisJournal);

        // ── 1. Prevenir ceux qui approchent de l'echeance ──
        //
        // « Derniere connexion, a defaut la creation » : un compte ouvert
        // et jamais utilise a bien une date a partir de laquelle compter.
        var aPrevenir = await bd.Users
            .Where(u => (u.LastLoginAt ?? u.CreatedAt) < seuilPreavis
                        && (u.LastLoginAt ?? u.CreatedAt) >= seuilCompte
                        && !u.PreavisSuppressionEnvoye)
            .Take(PreavisParPassage)
            .ToListAsync(arret);

        var prevenus = 0;
        foreach (var u in aPrevenir)
        {
            if (string.IsNullOrWhiteSpace(u.Email)) continue;
            var echeance = (u.LastLoginAt ?? u.CreatedAt).AddMonths(moisCompte);
            if (agit)
            {
                try
                {
                    await mail.Envoyer(ModelesCourriel.CompteInactif(u.Email, u.FirstName ?? "", echeance));
                    u.PreavisSuppressionEnvoye = true;
                    prevenus++;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Preavis d'inactivite non expedie a {Compte}", u.Id);
                }
            }
            else prevenus++;
        }
        if (agit && prevenus > 0) await bd.SaveChangesAsync(arret);

        // ── 2. Effacer les comptes restes sans reponse ──
        var aEffacer = await bd.Users
            .Where(u => (u.LastLoginAt ?? u.CreatedAt) < seuilCompte)
            .Take(PreavisParPassage)
            .ToListAsync(arret);

        var effaces = 0;
        var fichiers = 0;
        foreach (var u in aEffacer)
        {
            if (!agit) { effaces++; continue; }

            fichiers += depot.EffacerTousDe(u.Id);

            bd.Applications.RemoveRange(bd.Applications.Where(x => x.UserId == u.Id));
            bd.SavedSearches.RemoveRange(bd.SavedSearches.Where(x => x.UserId == u.Id));
            bd.Notifications.RemoveRange(bd.Notifications.Where(x => x.UserId == u.Id));
            bd.CvSections.RemoveRange(bd.CvSections.Where(x => x.UserId == u.Id));
            bd.CompanyFollows.RemoveRange(bd.CompanyFollows.Where(x => x.UserId == u.Id));
            bd.PushTokens.RemoveRange(bd.PushTokens.Where(x => x.UserId == u.Id));
            bd.JobNotes.RemoveRange(bd.JobNotes.Where(x => x.UserId == u.Id));
            bd.MessageTemplates.RemoveRange(bd.MessageTemplates.Where(x => x.UserId == u.Id));
            bd.UserSessions.RemoveRange(bd.UserSessions.Where(x => x.UserId == u.Id));
            if (!string.IsNullOrWhiteSpace(u.Email))
                bd.NewsletterSubscribers.RemoveRange(bd.NewsletterSubscribers.Where(s => s.Email == u.Email));
            await bd.SaveChangesAsync(arret);

            await comptes.DeleteAsync(u);
            effaces++;
        }

        // ── 3. Les candidatures closes depuis trop longtemps ──
        //
        // L'offre et ses compteurs restent : c'est la candidature — donc
        // le candidat — qui est concernee, pas le travail du recruteur.
        var candidatures = agit
            ? await bd.Applications
                .Where(x => x.AppliedAt < seuilCandidature)
                .ExecuteDeleteAsync(arret)
            : await bd.Applications.CountAsync(x => x.AppliedAt < seuilCandidature, arret);

        // ── 4. Le journal d'administration ──
        var traces = agit
            ? await bd.ActivityLogs
                .Where(x => x.CreatedAt < seuilJournal)
                .ExecuteDeleteAsync(arret)
            : await bd.ActivityLogs.CountAsync(x => x.CreatedAt < seuilJournal, arret);

        var quoi = $"{prevenus} préavis, {effaces} compte(s), {fichiers} fichier(s), "
                 + $"{candidatures} candidature(s), {traces} trace(s)";
        EtatDesServices.Noter("purge", true,
            agit ? "appliqué — " + quoi : "à blanc — aurait traité " + quoi);

        if (prevenus + effaces + candidatures + traces == 0)
        {
            _log.LogInformation("Purge : rien a traiter");
            return;
        }

        _log.LogInformation("Purge ({Mode}) : {Quoi}", agit ? "appliquee" : "a blanc", quoi);

        // Le journal garde des nombres, jamais des noms : la trace d'un
        // effacement ne doit pas reconstituer ce qu'on vient d'effacer.
        await journal.Log(agit ? "Purge" : "PurgeABlanc", "PlatformSetting", null, quoi,
                          null, "Conservation des données", null);
    }
}
