using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/import")]
[Authorize(Roles = "Admin")]
public class ImportController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportController> _logger;

    public ImportController(IServiceScopeFactory scopeFactory, ILogger<ImportController> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Admin : importer de vraies offres (lancé en arrière-plan pour éviter tout timeout).</summary>
    [HttpPost("jobs")]
    public IActionResult ImportJobs()
    {
        // Refus immédiat si un import tourne déjà, pour que l'admin le voie plutôt
        // que de croire son déclenchement pris en compte. Le verrou du service
        // reste la vraie garantie : ce test-ci ne fait qu'éviter le faux espoir.
        if (JobImportService.IsRunning)
            return Conflict(new { message = "Un import est déjà en cours. Attendez qu'il se termine." });

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<JobImportService>();
                var outcome = await svc.ImportAllAsync(CancellationToken.None);
                if (outcome.started)
                    _logger.LogInformation("Import admin terminé : {N} offres ajoutées.", outcome.added);
                else
                    _logger.LogWarning("Import admin abandonné : un autre import était en cours.");
            }
            catch (Exception ex) { _logger.LogError(ex, "Import admin en échec"); }
        });
        return Accepted(new { message = "Import lancé en arrière-plan. Les nouvelles offres apparaîtront dans quelques minutes." });
    }

    /// <summary>Admin : compter les offres importées en double. Lecture seule, ne modifie rien.</summary>
    [HttpGet("duplicates")]
    public async Task<ActionResult<object>> Duplicates()
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<JobImportService>();
        return Ok(await svc.AnalyzeDuplicatesAsync(HttpContext.RequestAborted));
    }

    /// <summary>
    /// Admin : supprimer les exemplaires en double, en gardant le plus ancien.
    ///
    /// Simulation par defaut : il faut <c>apply=true</c> pour que quoi que ce soit
    /// soit ecrit. Supprimer la moitie d'un catalogue ne doit pas tenir a une faute
    /// de frappe dans une URL.
    /// </summary>
    [HttpPost("duplicates/purge")]
    public async Task<ActionResult<object>> PurgeDuplicates(
        [FromQuery] bool apply = false,
        [FromQuery] int batchSize = 2000)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<JobImportService>();
        var outcome = await svc.PurgeDuplicatesAsync(apply, batchSize, CancellationToken.None);

        if (!outcome.started) return Conflict(outcome);

        if (outcome.applied)
            _logger.LogWarning("Purge admin des doublons : {Deleted} offres supprimees.", outcome.offersDeleted);

        return Ok(outcome);
    }

    /// <summary>Admin : diagnostic des sources d'import à clé (statut HTTP, nb de résultats).</summary>
    [HttpGet("diagnostics")]
    public async Task<ActionResult<object>> Diagnostics()
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<JobImportService>();
        return Ok(await svc.DiagnoseAsync(HttpContext.RequestAborted));
    }

    /// <summary>Admin : rétro-remplir le salaire chiffré (annuel €) des offres importées.</summary>
    [HttpPost("reparse-salaries")]
    public async Task<ActionResult<object>> ReparseSalaries()
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<JobImportService>();
        var n = await svc.ReparseSalariesAsync(force: true, HttpContext.RequestAborted);
        return Ok(new { updated = n, message = $"{n} offre(s) mise(s) à jour avec un salaire chiffré." });
    }
}
