using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>Import automatique des offres : au démarrage si la base est quasi vide, puis toutes les 6 h.</summary>
public class JobImportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobImportBackgroundService> _logger;

    public JobImportBackgroundService(IServiceScopeFactory scopeFactory, ILogger<JobImportBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisse l'app démarrer (migrations, seed…)
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        await RunOnce(startupOnly: true, stoppingToken);
        await ReparseSalaries(stoppingToken);

        await Entretenir(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnce(startupOnly: false, stoppingToken);
                // Apres l'import, pas avant : une offre revue chez sa
                // source a l'instant ne doit pas etre retiree parce que
                // l'entretien est passe cinq minutes plus tot.
                await Entretenir(stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* arrêt normal */ }
    }

    /// <summary>
    /// L'entretien du catalogue, apres chaque import.
    ///
    /// Deux nettoyages qui n'existaient pas et dont l'absence se voyait
    /// a l'ecran :
    ///
    ///   Les offres importees qu'on ne revoit plus chez leur source
    ///   restaient en ligne indefiniment. Un candidat postulait a un
    ///   poste pourvu depuis six mois, n'obtenait jamais de reponse, et
    ///   en tirait la conclusion qui s'impose sur le site entier.
    ///
    ///   Les mises en avant echues ne se retiraient pas. Le client
    ///   payait quinze jours et obtenait l'eternite ; a mesure que les
    ///   offres poussees s'accumulaient, le classement cessait de
    ///   vouloir dire quoi que ce soit.
    ///
    /// Enrobe : un entretien qui echoue ne doit pas emporter la boucle
    /// d'import avec lui.
    /// </summary>
    private async Task Entretenir(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            var qualite = scope.ServiceProvider.GetRequiredService<QualiteCatalogue>();
            var retirees = await qualite.ExpirerLesImportees();

            var facturation = scope.ServiceProvider.GetRequiredService<FacturationService>();
            var misesEnAvant = await facturation.RetirerLesEchues();

            // ── Signaler les nouvelles offres ──
            //
            // Le plan de site est relu quand le moteur le decide : au
            // mieux le lendemain, souvent la semaine suivante. Une offre
            // pourvue en quinze jours n'a pas ce temps devant elle.
            //
            // On ne signale que ce qui vient d'entrer, et pas plus d'un
            // millier : signaler tout le catalogue a chaque passage
            // serait traite comme du bruit, et a juste titre.
            var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
            var recentes = await db.JobOffers
                .Where(o => o.IsActive && !o.IsDraft
                            && o.ModerationStatus == "Approved"
                            && o.CreatedAt > DateTime.UtcNow.AddHours(-7))
                .OrderByDescending(o => o.Id)
                .Select(o => o.Id)
                .Take(1_000)
                .ToListAsync(ct);

            if (recentes.Count > 0)
            {
                var indexNow = scope.ServiceProvider.GetRequiredService<IndexNow>();
                await indexNow.SignalerOffres(recentes, ct);
            }

            if (retirees > 0 || misesEnAvant > 0)
                _logger.LogInformation(
                    "Entretien du catalogue : {Retirees} offres perimees retirees, {Mises} mises en avant echues.",
                    retirees, misesEnAvant);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Entretien du catalogue en échec");
        }
    }

    /// <summary>
    /// Le salaire chiffré des offres importées.
    ///
    /// Deux passes de nature différente. La première comble les manques :
    /// une offre arrivée sans salaire chiffré en reçoit un, c'est rapide et
    /// cela tourne à chaque démarrage.
    ///
    /// La seconde reprend tout, et ne tourne qu'une fois par version
    /// d'analyseur. C'est elle qui manquait : corriger ParseFtSalary ne
    /// valait jusqu'ici que pour les offres importées ensuite, les
    /// anciennes gardant indéfiniment leur valeur fausse. Le seul recours
    /// était un bouton d'administration — donc un mot de passe, donc jamais.
    /// Le numéro de version enregistré en base fait que le code et les
    /// données ne peuvent plus diverger sans qu'on s'en aperçoive.
    /// </summary>
    private async Task ReparseSalaries(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<JobImportService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var n = await svc.ReparseSalariesAsync(force: false, ct);
            if (n > 0) _logger.LogInformation("Salaires chiffrés au démarrage : {N} offres.", n);

            var reglage = await db.PlatformSettings
                .FirstOrDefaultAsync(s => s.Key == JobImportService.CleVersionSalaire, ct);
            var appliquee = int.TryParse(reglage?.Value, out var v) ? v : 0;

            if (appliquee >= JobImportService.VersionAnalyseSalaire) return;

            _logger.LogInformation(
                "Analyseur de salaires en version {Neuve}, base en version {Ancienne} : reprise complète du corpus.",
                JobImportService.VersionAnalyseSalaire, appliquee);

            var corriges = await svc.ReparseSalariesAsync(force: true, ct);

            // Enregistré seulement après coup : une reprise interrompue par
            // un arrêt du serveur doit se rejouer au démarrage suivant, pas
            // se croire faite à moitié.
            if (reglage == null)
            {
                db.PlatformSettings.Add(new PlatformSetting
                {
                    Key = JobImportService.CleVersionSalaire,
                    Value = JobImportService.VersionAnalyseSalaire.ToString(),
                    Type = "int",
                    Description = "Version de l'analyseur de salaires deja appliquee au corpus",
                });
            }
            else
            {
                reglage.Value = JobImportService.VersionAnalyseSalaire.ToString();
                reglage.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Reprise des salaires terminée : {N} offres recalculées, version {V} enregistrée.",
                corriges, JobImportService.VersionAnalyseSalaire);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Reparse salaires en échec");
        }
    }

    private async Task RunOnce(bool startupOnly, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (startupOnly)
            {
                var count = await db.JobOffers.CountAsync(j => j.IsActive && j.ModerationStatus == "Approved", ct);
                if (count >= 20)
                {
                    _logger.LogInformation("Import auto au démarrage ignoré ({Count} offres déjà présentes).", count);
                    return;
                }
            }

            var svc = scope.ServiceProvider.GetRequiredService<JobImportService>();
            var outcome = await svc.ImportAllAsync(ct);
            if (outcome.started)
                _logger.LogInformation("Import automatique terminé : {Added} offres ajoutées.", outcome.added);
            else
                _logger.LogWarning("Import automatique ignoré : un import était déjà en cours.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Import automatique en échec");
        }
    }
}
