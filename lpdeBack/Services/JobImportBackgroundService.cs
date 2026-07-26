using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;

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

        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnce(startupOnly: false, stoppingToken);
        }
        catch (OperationCanceledException) { /* arrêt normal */ }
    }

    private async Task ReparseSalaries(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<JobImportService>();
            var n = await svc.ReparseSalariesAsync(force: false, ct);
            if (n > 0) _logger.LogInformation("Salaires chiffrés au démarrage : {N} offres.", n);
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
            var added = await svc.ImportAllAsync(ct);
            _logger.LogInformation("Import automatique terminé : {Added} offres ajoutées.", added);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Import automatique en échec");
        }
    }
}
