using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/import")]
[Authorize(Roles = "Admin")]
public class ImportController : ControllerBase
{
    private readonly JobImportService _svc;
    public ImportController(JobImportService svc) => _svc = svc;

    /// <summary>Admin : importer de vraies offres depuis les API publiques configurées.</summary>
    [HttpPost("jobs")]
    public async Task<ActionResult<object>> ImportJobs()
    {
        var n = await _svc.ImportAllAsync(HttpContext.RequestAborted);
        return Ok(new { imported = n, message = n > 0 ? $"{n} nouvelle(s) offre(s) importée(s)." : "Aucune nouvelle offre (déjà à jour)." });
    }

    /// <summary>Admin : rétro-remplir le salaire chiffré (annuel €) des offres importées.</summary>
    [HttpPost("reparse-salaries")]
    public async Task<ActionResult<object>> ReparseSalaries()
    {
        var n = await _svc.ReparseSalariesAsync(force: true, HttpContext.RequestAborted);
        return Ok(new { updated = n, message = $"{n} offre(s) mise(s) à jour avec un salaire chiffré." });
    }
}
