using System.ComponentModel.DataAnnotations;

namespace lpdeBack.DTOs;

public class RegisterDto
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Role { get; set; } = "Candidate";

    [MaxLength(200)]
    public string? Company { get; set; }
}

public class LoginDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class UpdateProfileDto
{
    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [MaxLength(200)]
    public string? Company { get; set; }

    [MaxLength(500)]
    public string? Bio { get; set; }

    [MaxLength(150)]
    public string? Title { get; set; }

    [MaxLength(500)]
    public string? Skills { get; set; }

    public int? ExperienceYears { get; set; }

    [MaxLength(200)]
    public string? Education { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(300)]
    public string? LinkedInUrl { get; set; }

    [MaxLength(300)]
    public string? PortfolioUrl { get; set; }
}

public class ChangePasswordDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expiration { get; set; }
    public UserDto User { get; set; } = null!;
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public string? ResumeUrl { get; set; }
    public string? Title { get; set; }
    public string? Skills { get; set; }
    public int? ExperienceYears { get; set; }
    public string? Education { get; set; }
    public string? City { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? PortfolioUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOnline { get; set; }
}
