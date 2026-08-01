using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using lpdeBack.Models;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IWebHostEnvironment _env;

    public UploadController(UserManager<AppUser> userManager, IWebHostEnvironment env)
    {
        _userManager = userManager;
        _env = env;
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

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var uploadsDir = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "resumes");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{userId}_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
        var filePath = Path.Combine(uploadsDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var url = $"/uploads/resumes/{fileName}";

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
