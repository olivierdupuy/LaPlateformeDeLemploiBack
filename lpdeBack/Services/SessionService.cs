using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// L'emission et la revocation des jetons.
///
/// Le jeton se fabriquait dans le controleur, sans trace ni moyen de
/// retour : une fois signe, il vivait sept jours quoi qu'il arrive.
/// Suspendre un compte, changer un mot de passe ou desactiver la double
/// authentification ne fermaient aucune porte — ils la fermaient a partir
/// de la semaine suivante.
///
/// Chaque jeton porte desormais deux marques verifiees a chaque requete :
/// son identifiant propre (jti), qui correspond a une session qu'on peut
/// couper une par une ; et le tampon de securite du compte, qu'Identity
/// change de lui-meme des que le mot de passe ou la double authentification
/// bougent — ce qui invalide d'un coup tous les jetons existants.
/// </summary>
public class SessionService
{
    /// <summary>La revendication qui porte le tampon de securite du compte.</summary>
    public const string ClaimTampon = "sst";

    /// <summary>Marque un jeton qui ne sert qu'a franchir l'etape de double authentification.</summary>
    public const string ClaimDefi = "defi";

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly UserManager<AppUser> _userManager;

    public SessionService(AppDbContext context, IConfiguration config, UserManager<AppUser> userManager)
    {
        _context = context;
        _config = config;
        _userManager = userManager;
    }

    private TimeSpan DureeSession =>
        TimeSpan.FromDays(double.TryParse(_config["Jwt:DaysValid"], out var j) ? j : 7);

    // ══════════════════════════════════════
    //  Emission
    // ══════════════════════════════════════

    /// <summary>
    /// Ouvre une session et rend le jeton qui l'accompagne. La session est
    /// enregistree avant que le jeton ne soit remis : un jeton dont la
    /// session manquerait serait refuse des la requete suivante.
    /// </summary>
    public async Task<(string Token, DateTime Expiration)> Ouvrir(
        AppUser user, string methode, HttpContext? http,
        TimeSpan? duree = null, IEnumerable<Claim>? supplement = null)
    {
        var jti = Guid.NewGuid().ToString("N");
        var expiration = DateTime.UtcNow.Add(duree ?? DureeSession);
        var tampon = await _userManager.GetSecurityStampAsync(user);

        var agent = http?.Request.Headers.UserAgent.ToString();
        _context.UserSessions.Add(new UserSession
        {
            UserId = user.Id,
            Jti = jti,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            ExpiresAt = expiration,
            IpAddress = Ip(http),
            UserAgent = Tronquer(agent, 400),
            Device = DecrireAppareil(agent),
            Method = methode,
        });
        await _context.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.GivenName, user.FirstName),
            new(ClaimTypes.Surname, user.LastName),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(ClaimTampon, tampon ?? ""),
        };

        // La prise en main ajoute l'identite de l'administrateur : les deux
        // voyagent ensemble, c'est ce qui permet au journal de nommer
        // l'auteur reel d'une action faite sous un autre compte.
        if (supplement != null) claims.AddRange(supplement);

        return (Signer(claims, expiration), expiration);
    }

    /// <summary>
    /// Re-signe le jeton de la session en cours avec le tampon a jour.
    ///
    /// Identity renouvelle le tampon de securite des qu'on touche au mot de
    /// passe, a la cle d'authentification ou a la double authentification —
    /// c'est ce qui tue les autres sessions, et c'est voulu. Mais cela tue
    /// aussi celle qui vient de faire le geste : preparer la double
    /// authentification deconnectait la personne au milieu de son
    /// installation.
    ///
    /// La session ne change pas : meme ligne, meme jti, meme echeance. Seul
    /// le jeton est refait, avec le tampon neuf.
    /// </summary>
    public async Task<string?> Rafraichir(AppUser user, string? jti)
    {
        if (string.IsNullOrEmpty(jti)) return null;

        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.Jti == jti && s.UserId == user.Id && s.RevokedAt == null);
        if (session == null) return null;

        var tampon = await _userManager.GetSecurityStampAsync(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.GivenName, user.FirstName),
            new(ClaimTypes.Surname, user.LastName),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(ClaimTampon, tampon ?? ""),
        };
        return Signer(claims, session.ExpiresAt);
    }

    /// <summary>
    /// Le jeton de defi : il ne prouve qu'une chose, que le mot de passe a
    /// ete donne. Il n'ouvre aucune session, n'ouvre aucune page, et ne
    /// vaut que le temps de saisir un code a six chiffres.
    /// </summary>
    public string OuvrirDefi(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimDefi, "2fa"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        return Signer(claims, DateTime.UtcNow.AddMinutes(5));
    }

    /// <summary>Relit un jeton de defi et rend l'identifiant du compte, ou null.</summary>
    public string? LireDefi(string jeton)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(jeton, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _config["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _config["Jwt:Audience"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = Cle(),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);

            // Un jeton de session ne doit jamais servir de defi : sans cette
            // verification, quiconque possede un jeton ordinaire franchirait
            // l'etape de double authentification de n'importe quel compte.
            if (principal.FindFirstValue(ClaimDefi) != "2fa") return null;
            return principal.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        catch
        {
            return null;
        }
    }

    private SymmetricSecurityKey Cle() => new(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));

    private string Signer(IEnumerable<Claim> claims, DateTime expiration)
    {
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiration,
            signingCredentials: new SigningCredentials(Cle(), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ══════════════════════════════════════
    //  Revocation
    // ══════════════════════════════════════

    /// <summary>Coupe une session precise. Le jeton correspondant meurt a la requete suivante.</summary>
    public async Task<bool> Revoquer(int id, string userId, string raison)
    {
        var session = await _context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && s.RevokedAt == null);
        if (session == null) return false;

        session.RevokedAt = DateTime.UtcNow;
        session.RevokedReason = raison;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Coupe toutes les sessions d'un compte, sauf eventuellement celle qui
    /// donne l'ordre : se deconnecter de partout ne doit pas obliger a se
    /// reconnecter pour constater que ca a marche.
    /// </summary>
    public async Task<int> RevoquerToutes(string userId, string raison, string? saufJti = null)
    {
        var sessions = await _context.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && (saufJti == null || s.Jti != saufJti))
            .ToListAsync();

        foreach (var s in sessions)
        {
            s.RevokedAt = DateTime.UtcNow;
            s.RevokedReason = raison;
        }
        await _context.SaveChangesAsync();
        return sessions.Count;
    }

    // ══════════════════════════════════════
    //  Lecture
    // ══════════════════════════════════════

    public async Task<List<UserSession>> Lister(string userId, bool actives = true)
    {
        var q = _context.UserSessions.Where(s => s.UserId == userId);
        if (actives) q = q.Where(s => s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow);
        return await q.OrderByDescending(s => s.LastSeenAt).Take(50).ToListAsync();
    }

    /// <summary>
    /// Cet appareil s'est-il deja connecte ? Sert a n'alerter que sur les
    /// nouveautes : un courriel a chaque connexion serait ignore au bout de
    /// trois jours, et l'alerte qui compte passerait avec les autres.
    /// </summary>
    public async Task<bool> AppareilConnu(string userId, string? agent, string? ip)
    {
        var appareil = DecrireAppareil(agent);
        return await _context.UserSessions.AnyAsync(s =>
            s.UserId == userId && (s.Device == appareil || s.IpAddress == ip));
    }

    // ══════════════════════════════════════
    //  Lecture de l'agent
    // ══════════════════════════════════════

    public static string? Ip(HttpContext? http)
    {
        if (http == null) return null;
        // Derriere IIS et un proxy, l'adresse du client est dans l'en-tete ;
        // RemoteIpAddress vaudrait celle du proxy pour tout le monde.
        var transmise = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(transmise))
            return transmise.Split(',')[0].Trim();
        return http.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// « Chrome sur Windows » plutot que quatre cents caracteres de jargon.
    /// L'agent brut reste enregistre a cote : c'est lui qui fait foi, celui-ci
    /// ne sert qu'a se reconnaitre dans une liste.
    /// </summary>
    public static string DecrireAppareil(string? agent)
    {
        if (string.IsNullOrWhiteSpace(agent)) return "Appareil inconnu";

        var navigateur =
            agent.Contains("Edg/") ? "Edge"
            : agent.Contains("OPR/") || agent.Contains("Opera") ? "Opera"
            : agent.Contains("Chrome") && !agent.Contains("Chromium") ? "Chrome"
            : agent.Contains("Firefox") ? "Firefox"
            : agent.Contains("Safari") ? "Safari"
            : "Navigateur";

        var systeme =
            agent.Contains("Windows NT 10") || agent.Contains("Windows NT 11") ? "Windows"
            : agent.Contains("Windows") ? "Windows"
            : agent.Contains("iPhone") ? "iPhone"
            : agent.Contains("iPad") ? "iPad"
            : agent.Contains("Android") ? "Android"
            : agent.Contains("Mac OS X") ? "macOS"
            : agent.Contains("Linux") ? "Linux"
            : "systeme inconnu";

        return $"{navigateur} sur {systeme}";
    }

    // ══════════════════════════════════════
    //  Normalisation des codes
    // ══════════════════════════════════════

    /// <summary>
    /// Le code de l'application : six chiffres, debarrasses de l'espace que
    /// les applications intercalent a l'affichage (« 123 456 »).
    /// </summary>
    public static string CodeApplication(string? code) =>
        (code ?? "").Replace(" ", "").Replace("-", "").Trim();

    /// <summary>
    /// Le code de secours porte un tiret au milieu — « B44X2-3RP4G » — et
    /// Identity le compare tel quel. Le retirer comme on retire l'espace
    /// d'un code a six chiffres rendait tous les codes de secours faux :
    /// on ne l'enleve donc pas, on le remet meme quand il manque.
    /// </summary>
    public static string CodeDeSecours(string? code)
    {
        var c = (code ?? "").Replace(" ", "").Trim().ToUpperInvariant();
        if (c.Length == 10 && !c.Contains('-')) c = c[..5] + "-" + c[5..];
        return c;
    }

    private static string? Tronquer(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];
}
