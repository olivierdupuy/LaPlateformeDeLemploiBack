using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using lpdeBack.Models;
using lpdeBack.DTOs;
using lpdeBack.Data;
using lpdeBack.Hubs;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly IConfiguration _config;
    private readonly ActivityLogService _log;
    private readonly AppDbContext _context;

    public AuthController(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        IConfiguration config,
        ActivityLogService log,
        AppDbContext context)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
        _log = log;
        _context = context;
    }

    /// <summary>Register a new user</summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        // Check if registration is allowed
        var allowReg = await _context.PlatformSettings
            .Where(s => s.Key == "allow_registration")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        if (allowReg == "false")
            return BadRequest(new { message = "Les inscriptions sont actuellement fermees. Veuillez reessayer plus tard." });

        var existingUser = await _userManager.FindByEmailAsync(dto.Email);
        if (existingUser != null)
            return BadRequest(new { message = "Un compte avec cet email existe deja." });

        var validRoles = new[] { "Candidate", "Recruiter" };
        if (!validRoles.Contains(dto.Role))
            return BadRequest(new { message = "Role invalide. Utilisez 'Candidate' ou 'Recruiter'." });

        var user = new AppUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Role = dto.Role,
            Company = dto.Company
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        await _userManager.AddToRoleAsync(user, dto.Role);

        _ = _log.Log("Register", "User", null, $"Inscription: {user.FirstName} {user.LastName} ({dto.Role})", user.Id, $"{user.FirstName} {user.LastName}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(GenerateAuthResponse(user));
    }

    /// <summary>Login with email and password</summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null || !user.IsActive)
            return Unauthorized(new { message = "Email ou mot de passe incorrect." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: false);
        if (!result.Succeeded)
            return Unauthorized(new { message = "Email ou mot de passe incorrect." });

        _ = _log.Log("Login", "User", null, $"Connexion: {user.FirstName} {user.LastName}", user.Id, $"{user.FirstName} {user.LastName}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(GenerateAuthResponse(user));
    }

    /// <summary>Get current user profile</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> GetMe()
    {
        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user == null) return NotFound();

        return Ok(MapToUserDto(user));
    }

    /// <summary>Update current user profile</summary>
    [HttpPut("profile")]
    [Authorize]
    public async Task<ActionResult<UserDto>> UpdateProfile(UpdateProfileDto dto)
    {
        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user == null) return NotFound();

        if (dto.FirstName != null) user.FirstName = dto.FirstName;
        if (dto.LastName != null) user.LastName = dto.LastName;
        if (dto.AvatarUrl != null) user.AvatarUrl = dto.AvatarUrl;
        if (dto.Company != null) user.Company = dto.Company;
        if (dto.Bio != null) user.Bio = dto.Bio;
        if (dto.Title != null) user.Title = dto.Title;
        if (dto.Skills != null) user.Skills = dto.Skills;
        if (dto.ExperienceYears.HasValue) user.ExperienceYears = dto.ExperienceYears;
        if (dto.Education != null) user.Education = dto.Education;
        if (dto.City != null) user.City = dto.City;
        if (dto.LinkedInUrl != null) user.LinkedInUrl = dto.LinkedInUrl;
        if (dto.PortfolioUrl != null) user.PortfolioUrl = dto.PortfolioUrl;

        await _userManager.UpdateAsync(user);
        return Ok(MapToUserDto(user));
    }

    /// <summary>Change password</summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user == null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        return Ok(new { message = "Mot de passe modifie avec succes." });
    }

    /// <summary>Admin: list all users</summary>
    [HttpGet("users")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAllUsers()
    {
        var users = await _userManager.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
        return Ok(users.Select(u => { var dto = MapToUserDto(u); dto.IsOnline = ChatHub.IsUserOnline(u.Id); return dto; }));
    }

    /// <summary>Admin: get online user IDs</summary>
    [HttpGet("online-users")]
    [Authorize(Roles = "Admin")]
    public ActionResult<IEnumerable<string>> GetOnlineUsers()
    {
        return Ok(ChatHub.GetOnlineUserIds());
    }

    /// <summary>Admin: toggle user active status</summary>
    [HttpPatch("users/{id}/toggle-active")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ToggleUserActive(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        await _userManager.UpdateAsync(user);

        return Ok(new { user.Id, user.IsActive });
    }

    /// <summary>Admin: change user role</summary>
    [HttpPatch("users/{id}/role")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChangeUserRole(string id, [FromBody] ChangeRoleDto dto)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, dto.Role);
        user.Role = dto.Role;
        await _userManager.UpdateAsync(user);

        return Ok(MapToUserDto(user));
    }

    // ── Helpers ──

    private AuthResponseDto GenerateAuthResponse(AppUser user)
    {
        var token = GenerateJwtToken(user);
        return new AuthResponseDto
        {
            Token = token.Token,
            Expiration = token.Expiration,
            User = MapToUserDto(user)
        };
    }

    private (string Token, DateTime Expiration) GenerateJwtToken(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiration = DateTime.UtcNow.AddDays(7);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.GivenName, user.FirstName),
            new(ClaimTypes.Surname, user.LastName),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiration,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiration);
    }

    private static UserDto MapToUserDto(AppUser user) => new()
    {
        Id = user.Id,
        Email = user.Email!,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Role = user.Role,
        Company = user.Company,
        AvatarUrl = user.AvatarUrl,
        Bio = user.Bio,
        ResumeUrl = user.ResumeUrl,
        Title = user.Title,
        Skills = user.Skills,
        ExperienceYears = user.ExperienceYears,
        Education = user.Education,
        City = user.City,
        LinkedInUrl = user.LinkedInUrl,
        PortfolioUrl = user.PortfolioUrl,
        CreatedAt = user.CreatedAt
    };
}

// Small DTO for role change
public class ChangeRoleDto
{
    public string Role { get; set; } = string.Empty;
}
