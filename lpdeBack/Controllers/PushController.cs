using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PushController : ControllerBase
{
    private readonly AppDbContext _context;
    public PushController(AppDbContext context) => _context = context;
    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] PushTokenDto dto)
    {
        var userId = GetUserId();

        // Remove ALL old tokens for this user (one device = one token)
        var oldTokens = await _context.PushTokens.Where(t => t.UserId == userId).ToListAsync();
        if (oldTokens.Any(t => t.Token == dto.Token))
            return Ok(); // Already registered with same token

        _context.PushTokens.RemoveRange(oldTokens);
        _context.PushTokens.Add(new PushToken { UserId = userId, Token = dto.Token });
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister()
    {
        var userId = GetUserId();
        var tokens = await _context.PushTokens.Where(t => t.UserId == userId).ToListAsync();
        _context.PushTokens.RemoveRange(tokens);
        await _context.SaveChangesAsync();
        return Ok();
    }
}

public class PushTokenDto
{
    public string Token { get; set; } = string.Empty;
}
