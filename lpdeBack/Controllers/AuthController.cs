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
    private readonly SessionService _sessions;
    private readonly IEmailSender _mail;
    private readonly IHttpClientFactory _http;

    public AuthController(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        IConfiguration config,
        ActivityLogService log,
        AppDbContext context,
        SessionService sessions,
        IEmailSender mail,
        IHttpClientFactory http)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
        _log = log;
        _context = context;
        _sessions = sessions;
        _mail = mail;
        _http = http;
    }

    private string SiteUrl => (_config["App:PublicUrl"] ?? "").TrimEnd('/');
    private string? Ip() => SessionService.Ip(HttpContext);

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
            return BadRequest(new { message = "Les inscriptions sont actuellement fermées. Veuillez reessayer plus tard." });

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

        // La confirmation part tout de suite. Sans serveur configure, le
        // message est ecrit au journal : l'inscription reussit quand meme,
        // et l'interesse verra un bandeau l'invitant a la redemander.
        await EnvoyerConfirmation(user);

        await _log.Log("Register", "User", null, $"Inscription: {user.FirstName} {user.LastName} ({dto.Role})", user.Id, $"{user.FirstName} {user.LastName}", Ip());
        return Ok(await Reponse(user, "Password"));
    }

    /// <summary>
    /// Connexion par mot de passe.
    ///
    /// Elle se faisait avec « lockoutOnFailure: false » : rien ne comptait
    /// les echecs, rien ne ralentissait, un robot pouvait essayer un
    /// dictionnaire entier sur une adresse connue. Les echecs comptent
    /// desormais, et cinq de suite ferment la porte un quart d'heure.
    ///
    /// Quand la double authentification protege le compte, le mot de passe
    /// juste ne rend plus de jeton de session : il rend un defi de cinq
    /// minutes, qui n'ouvre rien d'autre que l'ecran du code.
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email);

        // Le compte suspendu ne se distingue pas d'un mot de passe faux :
        // dire « ce compte est suspendu » confirmerait son existence a qui
        // ne fait que deviner des adresses.
        if (user == null || !user.IsActive)
            return Unauthorized(new { message = "Email ou mot de passe incorrect." });

        if (await _userManager.IsLockedOutAsync(user))
            return StatusCode(StatusCodes.Status423Locked, new
            {
                message = "Trop de tentatives : ce compte est bloqué quinze minutes. Passez par « mot de passe oublié » si vous ne le retrouvez pas.",
                verrouilleJusquA = user.LockoutEnd,
            });

        var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
            return StatusCode(StatusCodes.Status423Locked, new
            {
                message = "Trop de tentatives : ce compte est bloqué quinze minutes. Passez par « mot de passe oublié » si vous ne le retrouvez pas.",
                verrouilleJusquA = user.LockoutEnd,
            });

        if (!result.Succeeded)
        {
            var restants = _userManager.Options.Lockout.MaxFailedAccessAttempts - user.AccessFailedCount;
            return Unauthorized(new
            {
                message = restants is > 0 and <= 2
                    ? $"Email ou mot de passe incorrect. Encore {restants} tentative{(restants > 1 ? "s" : "")} avant blocage."
                    : "Email ou mot de passe incorrect.",
            });
        }

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return Ok(new AuthResponseDto
            {
                RequiresTwoFactor = true,
                ChallengeToken = _sessions.OuvrirDefi(user),
                User = MapToUserDto(user),
            });
        }

        return Ok(await Reponse(user, "Password"));
    }

    /// <summary>
    /// Deuxieme temps de la connexion : le code.
    ///
    /// Un code de secours est accepte au meme titre que celui de
    /// l'application — c'est exactement le cas du telephone perdu — mais il
    /// est consomme, et l'interesse en est averti pour qu'il sache combien
    /// il lui en reste.
    /// </summary>
    [HttpPost("2fa/verifier")]
    public async Task<ActionResult<AuthResponseDto>> VerifierDeuxFacteurs(DefiDeuxFacteursDto dto)
    {
        var userId = _sessions.LireDefi(dto.ChallengeToken);
        if (userId == null)
            return Unauthorized(new { message = "Cette demande a expiré. Reprenez la connexion depuis le début." });

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
            return Unauthorized(new { message = "Compte indisponible." });

        if (await _userManager.IsLockedOutAsync(user))
            return StatusCode(StatusCodes.Status423Locked, new { message = "Trop de tentatives : ce compte est bloqué quinze minutes." });

        var parApplication = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider,
            SessionService.CodeApplication(dto.Code));

        var parSecours = false;
        if (!parApplication)
            parSecours = (await _userManager.RedeemTwoFactorRecoveryCodeAsync(
                user, SessionService.CodeDeSecours(dto.Code))).Succeeded;

        if (!parApplication && !parSecours)
        {
            // Un code faux compte comme un mot de passe faux : sans cela, le
            // second facteur serait le seul endroit ou l'on peut essayer un
            // million de combinaisons a six chiffres sans etre inquiete.
            await _userManager.AccessFailedAsync(user);
            return Unauthorized(new { message = "Code invalide. Saisissez le code affiché par votre application, ou l'un de vos codes de secours." });
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        var reponse = await Reponse(user, parSecours ? "Recovery" : "Password");

        if (parSecours)
        {
            var restants = await _userManager.CountRecoveryCodesAsync(user);
            await _log.Log("CodeDeSecoursUtilise", "User", null,
                $"Connexion par code de secours ({restants} restants)", user.Id,
                $"{user.FirstName} {user.LastName}", Ip());
        }

        return Ok(reponse);
    }

    // ═══════════════════════════════════════════
    //  MOT DE PASSE OUBLIE
    // ═══════════════════════════════════════════

    /// <summary>
    /// Demande de reinitialisation.
    ///
    /// La reponse est la meme que le compte existe ou non. Repondre « aucun
    /// compte a cette adresse » offrirait a n'importe qui la liste des
    /// adresses inscrites, une par essai.
    /// </summary>
    [HttpPost("mot-de-passe-oublie")]
    public async Task<IActionResult> MotDePasseOublie(MotDePasseOublieDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email ?? "");

        if (user != null && user.IsActive)
        {
            var jeton = await _userManager.GeneratePasswordResetTokenAsync(user);
            var lien = $"{SiteUrl}/reinitialiser-mot-de-passe" +
                       $"?id={Uri.EscapeDataString(user.Id)}&jeton={Uri.EscapeDataString(jeton)}";

            await _mail.Envoyer(ModelesCourriel.Reinitialisation(user.Email!, user.FirstName, lien, 30));
            await _log.Log("MotDePasseOublie", "User", null, "Demande de reinitialisation", user.Id,
                         $"{user.FirstName} {user.LastName}", Ip());
        }

        return Ok(new
        {
            message = "Si un compte existe à cette adresse, un message vient de partir. Le lien qu'il contient reste valable trente minutes.",
        });
    }

    /// <summary>Fin du parcours : le nouveau mot de passe, et toutes les sessions coupees.</summary>
    [HttpPost("reinitialiser-mot-de-passe")]
    public async Task<IActionResult> ReinitialiserMotDePasse(ReinitialisationDto dto)
    {
        var user = await _userManager.FindByIdAsync(dto.UserId ?? "");
        if (user == null)
            return BadRequest(new { message = "Ce lien n'est plus valable. Demandez-en un nouveau." });

        var resultat = await _userManager.ResetPasswordAsync(user, dto.Jeton ?? "", dto.NouveauMotDePasse ?? "");
        if (!resultat.Succeeded)
        {
            var expire = resultat.Errors.Any(e => e.Code == "InvalidToken");
            return BadRequest(new
            {
                message = expire
                    ? "Ce lien a expiré ou a déjà servi. Demandez-en un nouveau."
                    : string.Join(" ", resultat.Errors.Select(e => e.Description)),
            });
        }

        // Une reinitialisation sert le plus souvent a reprendre un compte
        // qu'on croit visite : laisser les sessions ouvertes reviendrait a
        // changer la serrure sans reprendre les cles.
        user.LastPasswordChangedAt = DateTime.UtcNow;
        // Un compte qui recupere son mot de passe par courriel prouve du
        // meme coup qu'il possede l'adresse.
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);

        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _sessions.RevoquerToutes(user.Id, "Mot de passe reinitialise");

        await _log.Log("MotDePasseReinitialise", "User", null, "Mot de passe reinitialise", user.Id,
                     $"{user.FirstName} {user.LastName}", Ip());

        return Ok(new { message = "Mot de passe enregistré. Vous pouvez vous connecter." });
    }

    /// <summary>Confirmation d'adresse depuis le lien recu.</summary>
    [HttpPost("confirmer-email")]
    public async Task<IActionResult> ConfirmerEmail(ConfirmationEmailDto dto)
    {
        var user = await _userManager.FindByIdAsync(dto.UserId ?? "");
        if (user == null)
            return BadRequest(new { message = "Ce lien n'est plus valable." });

        if (user.EmailConfirmed)
            return Ok(new { message = "Cette adresse était déjà confirmée." });

        var resultat = await _userManager.ConfirmEmailAsync(user, dto.Jeton ?? "");
        if (!resultat.Succeeded)
            return BadRequest(new { message = "Ce lien a expiré ou a déjà servi. Redemandez-en un depuis la page Sécurité." });

        return Ok(new { message = "Adresse confirmée." });
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
        if (dto.IsSearchable.HasValue) user.IsSearchable = dto.IsSearchable.Value;

        await _userManager.UpdateAsync(user);
        return Ok(MapToUserDto(user));
    }

    // Le changement de mot de passe a quitte ce controleur : il vit
    // desormais dans SecurityController, avec le reste de ce qu'on fait
    // pour proteger son compte. Il y ferme aussi les autres sessions et
    // rend un jeton neuf — ce que cette version-ci ne faisait pas, laissant
    // connecte quiconque avait le mot de passe qu'on venait de changer.

    /// <summary>RGPD : export des données personnelles de l'utilisateur (JSON).</summary>
    [HttpGet("export-data")]
    [Authorize]
    public async Task<IActionResult> ExportData()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var applications = await _context.Applications.Where(a => a.UserId == userId)
            .Select(a => new { a.Id, a.FullName, a.Email, a.Phone, a.Status, a.AppliedAt, a.CoverLetter }).ToListAsync();
        var savedSearches = await _context.SavedSearches.Where(s => s.UserId == userId).ToListAsync();
        var reviews = await _context.CompanyReviews.Where(r => r.AuthorUserId == userId)
            .Select(r => new { r.Company, r.OverallRating, r.Title, r.Body, r.CreatedAt }).ToListAsync();

        return Ok(new { profile = MapToUserDto(user), applications, savedSearches, reviews, exportedAt = DateTime.UtcNow });
    }

    /// <summary>RGPD : suppression du compte et des données personnelles associées.</summary>
    [HttpDelete("account")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        _context.Applications.RemoveRange(_context.Applications.Where(a => a.UserId == userId));
        _context.SavedSearches.RemoveRange(_context.SavedSearches.Where(s => s.UserId == userId));
        _context.Notifications.RemoveRange(_context.Notifications.Where(n => n.UserId == userId));
        _context.CvSections.RemoveRange(_context.CvSections.Where(c => c.UserId == userId));
        _context.CompanyFollows.RemoveRange(_context.CompanyFollows.Where(f => f.UserId == userId));
        _context.PushTokens.RemoveRange(_context.PushTokens.Where(p => p.UserId == userId));
        await _context.SaveChangesAsync();

        await _userManager.DeleteAsync(user);
        return Ok(new { message = "Compte supprimé." });
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

    /// <summary>SSO : connexion via un jeton Google Identity (One Tap / bouton Google).</summary>
    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> GoogleSignIn(GoogleSignInDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Credential))
            return BadRequest(new { message = "Jeton Google manquant." });

        // Vérifie le jeton d'identité auprès de Google
        using var http = new HttpClient();
        var resp = await http.GetAsync($"https://oauth2.googleapis.com/tokeninfo?id_token={Uri.EscapeDataString(dto.Credential)}");
        if (!resp.IsSuccessStatusCode)
            return Unauthorized(new { message = "Jeton Google invalide." });

        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
        if (string.IsNullOrEmpty(email))
            return Unauthorized(new { message = "Email Google introuvable." });

        // Vérifie l'audience si un Client ID est configuré
        var clientId = _config["Google:ClientId"];
        if (!string.IsNullOrEmpty(clientId) && root.TryGetProperty("aud", out var aud) && aud.GetString() != clientId)
            return Unauthorized(new { message = "Application Google non autorisée." });

        var given = root.TryGetProperty("given_name", out var g) ? g.GetString() : "";
        var family = root.TryGetProperty("family_name", out var f) ? f.GetString() : "";
        var sujet = root.TryGetProperty("sub", out var s) ? s.GetString() : null;

        return await EntrerParFournisseur("Google", sujet, email, given, family);
    }

    /// <summary>
    /// SSO LinkedIn (OpenID Connect). Le client obtient un code aupres de
    /// LinkedIn, nous l'echangeons ici contre un jeton d'identite : le
    /// secret de l'application ne quitte jamais le serveur.
    /// </summary>
    [HttpPost("linkedin")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> LinkedInSignIn(LinkedInDto dto)
    {
        var clientId = _config["LinkedIn:ClientId"];
        var secret = _config["LinkedIn:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            return StatusCode(StatusCodes.Status501NotImplemented,
                new { message = "La connexion LinkedIn n'est pas configurée sur ce serveur." });

        if (string.IsNullOrWhiteSpace(dto.Code))
            return BadRequest(new { message = "Code LinkedIn manquant." });

        var http = _http.CreateClient();

        var echange = await http.PostAsync("https://www.linkedin.com/oauth/v2/accessToken",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = dto.Code,
                ["redirect_uri"] = dto.RedirectUri,
                ["client_id"] = clientId,
                ["client_secret"] = secret,
            }));

        if (!echange.IsSuccessStatusCode)
            return Unauthorized(new { message = "LinkedIn a refusé l'échange. Reprenez la connexion." });

        using var jetonDoc = System.Text.Json.JsonDocument.Parse(await echange.Content.ReadAsStringAsync());
        if (!jetonDoc.RootElement.TryGetProperty("access_token", out var acces))
            return Unauthorized(new { message = "Réponse LinkedIn inattendue." });

        // « userinfo » est le point OpenID standard : il rend l'identifiant,
        // l'adresse et le nom sans avoir a demander de permission de plus.
        using var requete = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
        requete.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", acces.GetString());
        var profil = await http.SendAsync(requete);
        if (!profil.IsSuccessStatusCode)
            return Unauthorized(new { message = "Profil LinkedIn illisible." });

        using var doc = System.Text.Json.JsonDocument.Parse(await profil.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
        if (string.IsNullOrEmpty(email))
            return Unauthorized(new { message = "LinkedIn n'a pas communiqué d'adresse e-mail." });

        return await EntrerParFournisseur(
            "LinkedIn",
            root.TryGetProperty("sub", out var s) ? s.GetString() : null,
            email,
            root.TryGetProperty("given_name", out var g) ? g.GetString() : "",
            root.TryGetProperty("family_name", out var f) ? f.GetString() : "");
    }

    // ── Helpers ──

    /// <summary>
    /// Entree par un fournisseur externe.
    ///
    /// La version precedente rapprochait les comptes sur la seule adresse
    /// e-mail. Le rapprochement se fait maintenant sur l'identifiant que le
    /// fournisseur donne au compte (« sub »), enregistre dans la table des
    /// connexions externes d'Identity : une adresse peut changer de mains,
    /// pas cet identifiant-la.
    ///
    /// La double authentification s'applique aussi ici : la contourner par
    /// un bouton « Continuer avec Google » la rendrait decorative.
    /// </summary>
    private async Task<ActionResult<AuthResponseDto>> EntrerParFournisseur(
        string fournisseur, string? sujet, string email, string? prenom, string? nom)
    {
        AppUser? user = null;

        if (!string.IsNullOrEmpty(sujet))
            user = await _userManager.FindByLoginAsync(fournisseur, sujet);

        user ??= await _userManager.FindByEmailAsync(email);

        if (user == null)
        {
            var ouvert = await _context.PlatformSettings
                .Where(s => s.Key == "allow_registration").Select(s => s.Value).FirstOrDefaultAsync();
            if (ouvert == "false")
                return BadRequest(new { message = "Les inscriptions sont actuellement fermées." });

            user = new AppUser
            {
                UserName = email,
                Email = email,
                // Le fournisseur a deja verifie l'adresse : la faire
                // reconfirmer serait demander deux fois la meme preuve.
                EmailConfirmed = true,
                FirstName = prenom ?? "",
                LastName = nom ?? "",
                Role = "Candidate",
            };
            var creation = await _userManager.CreateAsync(user);
            if (!creation.Succeeded)
                return BadRequest(new { message = "Création du compte impossible." });
            await _userManager.AddToRoleAsync(user, "Candidate");
        }

        if (!user.IsActive)
            return Unauthorized(new { message = "Ce compte est suspendu." });

        // Lie le compte au fournisseur si ce n'est pas deja fait : la
        // prochaine entree se fera sur l'identifiant, plus sur l'adresse.
        if (!string.IsNullOrEmpty(sujet))
        {
            var connus = await _userManager.GetLoginsAsync(user);
            if (!connus.Any(l => l.LoginProvider == fournisseur))
                await _userManager.AddLoginAsync(user, new UserLoginInfo(fournisseur, sujet, fournisseur));
        }

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return Ok(new AuthResponseDto
            {
                RequiresTwoFactor = true,
                ChallengeToken = _sessions.OuvrirDefi(user),
                User = MapToUserDto(user),
            });
        }

        return Ok(await Reponse(user, fournisseur));
    }

    /// <summary>Ouvre la session, note la connexion, alerte si l'appareil est neuf.</summary>
    private async Task<AuthResponseDto> Reponse(AppUser user, string methode)
    {
        var agent = Request.Headers.UserAgent.ToString();
        var ip = Ip();

        // A verifier avant d'ouvrir la session : celle qu'on ouvre rendrait
        // aussitot l'appareil « connu ».
        var connu = await _sessions.AppareilConnu(user.Id, agent, ip);

        var (jeton, expiration) = await _sessions.Ouvrir(user, methode, HttpContext);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        await _log.Log("Login", "User", null, $"Connexion: {user.FirstName} {user.LastName}", user.Id,
                     $"{user.FirstName} {user.LastName}", ip);

        // Un courriel a chaque connexion serait ignore au bout de trois
        // jours, et l'alerte qui compte passerait avec les autres.
        if (!connu)
        {
            await _mail.Envoyer(ModelesCourriel.NouvelleConnexion(
                user.Email!, user.FirstName, SessionService.DecrireAppareil(agent),
                ip ?? "inconnue", DateTime.UtcNow, $"{SiteUrl}/securite"));
        }

        return new AuthResponseDto { Token = jeton, Expiration = expiration, User = MapToUserDto(user) };
    }

    private async Task EnvoyerConfirmation(AppUser user)
    {
        var jeton = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var lien = $"{SiteUrl}/confirmer-email" +
                   $"?id={Uri.EscapeDataString(user.Id)}&jeton={Uri.EscapeDataString(jeton)}";
        await _mail.Envoyer(ModelesCourriel.Confirmation(user.Email!, user.FirstName, lien));
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
        IsSearchable = user.IsSearchable,
        CreatedAt = user.CreatedAt,
        EmailConfirmed = user.EmailConfirmed,
        TwoFactorEnabled = user.TwoFactorEnabled,
    };
}

// Small DTO for role change
public class ChangeRoleDto
{
    public string Role { get; set; } = string.Empty;
}
