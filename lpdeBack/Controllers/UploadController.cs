using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly DepotFichiers _depot;

    public UploadController(UserManager<AppUser> userManager, DepotFichiers depot)
    {
        _userManager = userManager;
        _depot = depot;
    }

    [HttpPost("resume")]
    public async Task<IActionResult> UploadResume(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Aucun fichier envoye.");

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest("Le fichier ne doit pas depasser 5 Mo.");

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".pdf")
            return BadRequest("Seuls les fichiers PDF sont acceptes.");

        // Le nom du fichier ne suffit pas a dire ce qu'il contient. On
        // lit l'en-tete : un vrai PDF commence par « %PDF- ». Cela ne
        // remplace pas un antivirus, mais ferme la porte aux fichiers
        // simplement renommes.
        await using (var controle = file.OpenReadStream())
        {
            var entete = new byte[5];
            var lus = await controle.ReadAsync(entete);
            if (lus < 5 || entete[0] != 0x25 || entete[1] != 0x50 || entete[2] != 0x44 || entete[3] != 0x46 || entete[4] != 0x2D)
                return BadRequest("Ce fichier n'est pas un PDF, quel que soit son nom.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Les depots precedents de ce membre s'en vont : un nouveau CV
        // portait un nouveau nom sans effacer l'ancien, et toutes les
        // versions restaient lisibles.
        _depot.EffacerTousDe(userId);

        var fileName = $"{userId}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        var filePath = _depot.Destination(fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var url = DepotFichiers.Prefixe + fileName;

        // Update user's ResumeUrl
        var user = await _userManager.FindByIdAsync(userId);
        if (user != null)
        {
            user.ResumeUrl = url;
            await _userManager.UpdateAsync(user);
        }

        return Ok(new { url });
    }
}
