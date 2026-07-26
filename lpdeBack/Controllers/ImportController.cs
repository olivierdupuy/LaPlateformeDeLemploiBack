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
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<JobImportService>();
                var n = await svc.ImportAllAsync(CancellationToken.None);
                _logger.LogInformation("Import admin terminé : {N} offres ajoutées.", n);
            }
            catch (Exception ex) { _logger.LogError(ex, "Import admin en échec"); }
        });
        return Accepted(new { message = "Import lancé en arrière-plan. Les nouvelles offres apparaîtront dans quelques minutes." });
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
