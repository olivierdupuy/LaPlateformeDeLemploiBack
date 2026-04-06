using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.DTOs;
using LaPlateformeDeLemploiAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace LaPlateformeDeLemploiAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    [HttpPost("register/jobseeker")]
    public async Task<ActionResult<AuthResponseDto>> RegisterJobSeeker(RegisterJobSeekerDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            return BadRequest(new { message = "Cet email est deja utilise." });

        var user = new User
        {
            Email = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Phone = dto.Phone,
            Role = "JobSeeker",
            Bio = dto.Bio,
            Skills = dto.Skills
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return Ok(GenerateAuthResponse(user));
    }

    [HttpPost("register/company")]
    public async Task<ActionResult<AuthResponseDto>> RegisterCompany(RegisterCompanyDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            return BadRequest(new { message = "Cet email est deja utilise." });

        var company = new Company
        {
            Name = dto.CompanyName,
            Description = dto.CompanyDescription,
            Website = dto.CompanyWebsite,
            Location = dto.CompanyLocation,
            LogoUrl = $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(dto.CompanyName)}&background=065f46&color=fff&size=128"
        };

        _context.Companies.Add(company);
        await _context.SaveChangesAsync();

        var user = new User
        {
            Email = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Phone = dto.Phone,
            Role = "Company",
            CompanyId = company.Id
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return Ok(GenerateAuthResponse(user, company.Name));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var user = await _context.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { message = "Email ou mot de passe incorrect." });

        return Ok(GenerateAuthResponse(user, user.Company?.Name));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetCurrentUser()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _context.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound();

        return Ok(new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            Phone = user.Phone,
            CompanyId = user.CompanyId,
            CompanyName = user.Company?.Name,
            Bio = user.Bio,
            Skills = user.Skills
        });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
        // Toujours retourner OK pour ne pas reveler si l'email existe
        if (user == null) return Ok(new { message = "Si cet email existe, un lien de reinitialisation a ete genere." });

        user.ResetToken = Guid.NewGuid().ToString("N");
        user.ResetTokenExpiry = DateTime.UtcNow.AddHours(1);
        await _context.SaveChangesAsync();

        // En production: envoyer un email avec le token
        // Pour le dev, on retourne le token dans la reponse
        return Ok(new { message = "Si cet email existe, un lien de reinitialisation a ete genere.", token = user.ResetToken });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
            return BadRequest(new { message = "Le mot de passe doit contenir au moins 6 caracteres." });

        var user = await _context.Users.FirstOrDefaultAsync(u =>
            u.ResetToken == dto.Token && u.ResetTokenExpiry > DateTime.UtcNow);

        if (user == null)
            return BadRequest(new { message = "Lien invalide ou expire. Veuillez refaire une demande." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.LastPasswordChange = DateTime.UtcNow;
        user.ResetToken = null;
        user.ResetTokenExpiry = null;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Mot de passe reinitialise avec succes." });
    }

    private AuthResponseDto GenerateAuthResponse(User user, string? companyName = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(ClaimTypes.GivenName, user.FirstName),
            new Claim(ClaimTypes.Surname, user.LastName)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new AuthResponseDto
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role,
                Phone = user.Phone,
                CompanyId = user.CompanyId,
                CompanyName = companyName,
                Bio = user.Bio,
                Skills = user.Skills
            }
        };
    }
}
