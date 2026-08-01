using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Ce qu'une personne peut faire pour proteger son propre compte.
///
/// Rien de tout cela n'existait : pas de double authentification, pas de
/// liste d'appareils, aucun moyen de fermer une session a distance, et le
/// seul recours contre un mot de passe oublie etait de deranger un
/// administrateur.
///
/// Ces gestes appartiennent a l'interesse, pas a l'administration : ils
/// vivent donc ici, derriere son propre jeton, et l'administration n'a que
/// les deux pouvoirs qu'elle ne peut pas ne pas avoir — deverrouiller, et
/// couper la double authentification de quelqu'un qui a perdu telephone et
/// codes de secours a la fois.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SecurityController : ControllerBase
{
    private const string Emetteur = "La Plateforme de l'emploi";
    private const int NombreCodesDeSecours = 10;

    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _context;
    private readonly SessionService _sessions;
    private readonly IEmailSender _mail;
    private readonly ActivityLogService _log;
    private readonly IConfiguration _config;

    public SecurityController(
        UserManager<AppUser> userManager,
        AppDbContext context,
        SessionService sessions,
        IEmailSender mail,
        ActivityLogService log,
        IConfiguration config)
    {
        _userManager = userManager;
        _context = context;
        _sessions = sessions;
        _mail = mail;
        _log = log;
        _config = config;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string? JtiCourant => User.FindFirstValue(JwtRegisteredClaimNames.Jti);
    private string LienSecurite => $"{(_config["App:PublicUrl"] ?? "").TrimEnd('/')}/securite";

    private async Task<AppUser?> Moi() => await _userManager.FindByIdAsync(UserId);

    // ═══════════════════════════════════════════
    //  1. ETAT DU COMPTE
    // ═══════════════════════════════════════════

    /// <summary>
    /// Tout ce qui touche a la securite de ce compte, en une reponse.
    /// La page qui l'affiche en a besoin d'un seul tenant : la servir en
    /// cinq appels ferait clignoter cinq blocs a l'ouverture.
    /// </summary>
    [HttpGet("etat")]
    public async Task<ActionResult<object>> Etat()
    {
        var user = await Moi();
        if (user == null) return NotFound();

        var sessions = await _sessions.Lister(user.Id);
        var deuxFacteurs = await _userManager.GetTwoFactorEnabledAsync(user);

        return Ok(new
        {
            email = user.Email,
            emailConfirme = user.EmailConfirmed,
            role = user.Role,

            deuxFacteurs,
            deuxFacteursDepuis = user.TwoFactorEnabledAt,
            codesDeSecoursRestants = deuxFacteurs ? await _userManager.CountRecoveryCodesAsync(user) : 0,

            // Un administrateur n'a pas le choix : la fiche doit le dire
            // avant qu'une garde ne l'y force sans explication.
            deuxFacteursObligatoire = user.Role == "Admin",

            motDePasseModifieLe = user.LastPasswordChangedAt,
            aUnMotDePasse = await _userManager.HasPasswordAsync(user),
            connexionsExternes = (await _userManager.GetLoginsAsync(user))
                .Select(l => new { fournisseur = l.LoginProvider, nom = l.ProviderDisplayName }),

            verrouilleJusquA = user.LockoutEnd,
            echecsRecents = user.AccessFailedCount,

            derniereConnexion = user.LastLoginAt,
            sessions = sessions.Select(s => new
            {
                s.Id, s.Device, s.IpAddress, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.Method,
                // « Cet appareil-ci » : sans ce reperage, on ferme la sienne
                // en croyant fermer celle d'un intrus.
                courante = s.Jti == JtiCourant,
            }),
        });
    }

    // ═══════════════════════════════════════════
    //  2. DOUBLE AUTHENTIFICATION
    // ═══════════════════════════════════════════

    /// <summary>
    /// Prepare l'activation : fabrique une cle et rend de quoi l'installer,
    /// en QR comme a la main. Rien n'est active a ce stade — tant que le
    /// premier code n'a pas ete verifie, on ne sait pas si l'application a
    /// bien enregistre la cle, et activer sur cette ignorance enfermerait
    /// la personne dehors.
    /// </summary>
    [HttpPost("2fa/preparer")]
    public async Task<ActionResult<object>> Preparer2fa()
    {
        var user = await Moi();
        if (user == null) return NotFound();

        if (await _userManager.GetTwoFactorEnabledAsync(user))
            return Conflict(new { message = "La double authentification est déjà active sur ce compte." });

        await _userManager.ResetAuthenticatorKeyAsync(user);
        var cle = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(cle))
            return Problem("La clé n'a pas pu être générée.");

        var uri = string.Format(
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6&period=30",
            UrlEncoder.Default.Encode(Emetteur),
            UrlEncoder.Default.Encode(user.Email!),
            cle);

        return Ok(new
        {
            cle,
            // Par groupes de quatre : une cle de trente-deux caracteres se
            // recopie a la main quand le telephone ne peut pas photographier.
            cleLisible = string.Join(" ", Decouper(cle, 4)),
            uri,
            token = await _sessions.Rafraichir(user, JtiCourant),
        });
    }

    /// <summary>Active, apres avoir verifie que l'application produit bien le bon code.</summary>
    [HttpPost("2fa/activer")]
    public async Task<ActionResult<object>> Activer2fa([FromBody] CodeDto dto)
    {
        var user = await Moi();
        if (user == null) return NotFound();

        var bon = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider,
            SessionService.CodeApplication(dto.Code));

        if (!bon)
            return BadRequest(new { message = "Ce code ne correspond pas. Vérifiez l'heure de votre téléphone : un décalage de plus d'une minute suffit à le fausser." });

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        user.TwoFactorEnabledAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var codes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, NombreCodesDeSecours))?.ToList()
                    ?? new List<string>();

        await _mail.Envoyer(ModelesCourriel.DoubleAuthentification(user.Email!, user.FirstName, true, LienSecurite));
        await _log.Log("2faActivee", "User", null, "Double authentification activee", user.Id,
                     $"{user.FirstName} {user.LastName}", SessionService.Ip(HttpContext));

        return Ok(new
        {
            message = "La double authentification est active.",
            codesDeSecours = codes,
            token = await _sessions.Rafraichir(user, JtiCourant),
        });
    }

    /// <summary>
    /// Coupe la double authentification. Le mot de passe est exige : sans
    /// lui, un jeton vole suffirait a retirer la protection qu'il est cense
    /// ne pas pouvoir franchir.
    /// </summary>
    [HttpPost("2fa/desactiver")]
    public async Task<IActionResult> Desactiver2fa([FromBody] MotDePasseEtCodeDto dto)
    {
        var user = await Moi();
        if (user == null) return NotFound();

        // Un administrateur ne peut pas se decouvrir : son compte voit toute
        // la base et peut prendre la main sur n'importe qui.
        if (user.Role == "Admin")
            return BadRequest(new { message = "La double authentification est obligatoire pour les administrateurs. Elle ne peut pas être désactivée sur ce compte." });

        if (!await _userManager.CheckPasswordAsync(user, dto.MotDePasse ?? ""))
            return BadRequest(new { message = "Mot de passe incorrect." });

        // Le mot de passe seul ne suffit pas a retirer la protection qui le
        // double : sinon un mot de passe devine rendrait la double
        // authentification inutile. Un code de secours convient — c'est
        // precisement le cas du telephone perdu.
        var bon = await _userManager.VerifyTwoFactorTokenAsync(
                      user, _userManager.Options.Tokens.AuthenticatorTokenProvider,
                      SessionService.CodeApplication(dto.Code))
                  || (await _userManager.RedeemTwoFactorRecoveryCodeAsync(
                      user, SessionService.CodeDeSecours(dto.Code))).Succeeded;

        if (!bon)
            return BadRequest(new { message = "Code invalide. Saisissez le code affiché par votre application, ou l'un de vos codes de secours." });

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        user.TwoFactorEnabledAt = null;
        await _userManager.UpdateAsync(user);

        await _mail.Envoyer(ModelesCourriel.DoubleAuthentification(user.Email!, user.FirstName, false, LienSecurite));
        await _log.Log("2faDesactivee", "User", null, "Double authentification desactivee", user.Id,
                     $"{user.FirstName} {user.LastName}", SessionService.Ip(HttpContext));

        return Ok(new
        {
            message = "La double authentification est désactivée.",
            token = await _sessions.Rafraichir(user, JtiCourant),
        });
    }

    /// <summary>
    /// Refabrique les codes de secours. Les anciens cessent aussitot de
    /// valoir : c'est le seul comportement sur, puisqu'on regenere
    /// justement quand on soupconne les precedents d'avoir ete vus.
    /// </summary>
    [HttpPost("2fa/codes-de-secours")]
    public async Task<ActionResult<object>> RegenererCodes([FromBody] MotDePasseDto dto)
    {
        var user = await Moi();
        if (user == null) return NotFound();

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
            return BadRequest(new { message = "La double authentification n'est pas active sur ce compte." });

        if (await _userManager.HasPasswordAsync(user)
            && !await _userManager.CheckPasswordAsync(user, dto.MotDePasse ?? ""))
            return BadRequest(new { message = "Mot de passe incorrect." });

        var codes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, NombreCodesDeSecours))?.ToList()
                    ?? new List<string>();

        await _log.Log("CodesDeSecours", "User", null, "Codes de secours regeneres", user.Id,
                     $"{user.FirstName} {user.LastName}", SessionService.Ip(HttpContext));

        return Ok(new { codesDeSecours = codes, token = await _sessions.Rafraichir(user, JtiCourant) });
    }

    // ═══════════════════════════════════════════
    //  3. MOT DE PASSE
    // ═══════════════════════════════════════════

    /// <summary>
    /// Change le mot de passe et ferme les autres sessions.
    ///
    /// Changer son mot de passe parce qu'on le croit connu d'un autre et
    /// laisser cet autre connecte n'a aucun sens : Identity renouvelle le
    /// tampon de securite, ce qui coupe tous les jetons, et l'on en emet
    /// un neuf pour celui qui vient d'agir.
    /// </summary>
    [HttpPost("mot-de-passe")]
    public async Task<ActionResult<object>> ChangerMotDePasse([FromBody] ChangementMotDePasseDto dto)
    {
        var user = await Moi();
        if (user == null) return NotFound();

        var resultat = await _userManager.ChangePasswordAsync(user, dto.Actuel ?? "", dto.Nouveau ?? "");
        if (!resultat.Succeeded)
            return BadRequest(new { message = string.Join(" ", resultat.Errors.Select(e => e.Description)) });

        user.LastPasswordChangedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        await _sessions.RevoquerToutes(user.Id, "Mot de passe modifie", JtiCourant);
        var jeton = await _sessions.Rafraichir(user, JtiCourant);

        await _mail.Envoyer(ModelesCourriel.MotDePasseChange(user.Email!, user.FirstName, LienSecurite));
        await _log.Log("MotDePasseChange", "User", null, "Mot de passe modifie", user.Id,
                     $"{user.FirstName} {user.LastName}", SessionService.Ip(HttpContext));

        // Le tampon a change : l'ancien jeton est mort a l'instant meme.
        // Sans celui-ci en retour, la personne serait deconnectee pour avoir
        // fait exactement ce qu'on lui demandait.
        return Ok(new { message = "Mot de passe modifié. Vos autres appareils ont été déconnectés.", token = jeton });
    }

    // ═══════════════════════════════════════════
    //  4. SESSIONS
    // ═══════════════════════════════════════════

    [HttpDelete("sessions/{id:int}")]
    public async Task<IActionResult> FermerSession(int id)
    {
        var ok = await _sessions.Revoquer(id, UserId, "Fermee depuis la page Securite");
        if (!ok) return NotFound(new { message = "Cette session n'existe plus." });
        return Ok(new { message = "Appareil déconnecté." });
    }

    /// <summary>Ferme tout, sauf l'appareil qui donne l'ordre.</summary>
    [HttpPost("sessions/tout-fermer")]
    public async Task<ActionResult<object>> FermerToutesLesSessions()
    {
        var n = await _sessions.RevoquerToutes(UserId, "Deconnexion de tous les appareils", JtiCourant);
        return Ok(new
        {
            fermees = n,
            message = n == 0
                ? "Aucun autre appareil n'était connecté."
                : n == 1 ? "Un autre appareil a été déconnecté."
                : $"{n} autres appareils ont été déconnectés.",
        });
    }

    // ═══════════════════════════════════════════
    //  5. ADRESSE E-MAIL
    // ═══════════════════════════════════════════

    /// <summary>(Re)envoie le lien de confirmation d'adresse.</summary>
    [HttpPost("email/confirmer")]
    public async Task<ActionResult<object>> EnvoyerConfirmation()
    {
        var user = await Moi();
        if (user == null) return NotFound();
        if (user.EmailConfirmed)
            return Ok(new { message = "Cette adresse est déjà confirmée." });

        var jeton = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var lien = $"{(_config["App:PublicUrl"] ?? "").TrimEnd('/')}/confirmer-email" +
                   $"?id={Uri.EscapeDataString(user.Id)}&jeton={Uri.EscapeDataString(jeton)}";

        var parti = await _mail.Envoyer(ModelesCourriel.Confirmation(user.Email!, user.FirstName, lien));
        return Ok(new
        {
            envoye = parti,
            message = parti
                ? "Un message vient de partir. Ouvrez le lien qu'il contient."
                : "Aucun serveur d'expédition n'est configuré : le message a été écrit dans le journal du serveur.",
        });
    }

    // ── Aides ──

    private static IEnumerable<string> Decouper(string s, int taille)
    {
        for (var i = 0; i < s.Length; i += taille)
            yield return s.Substring(i, Math.Min(taille, s.Length - i));
    }
}

// ── DTO ──

public class CodeDto { public string? Code { get; set; } }
public class MotDePasseDto { public string? MotDePasse { get; set; } }
public class MotDePasseEtCodeDto { public string? MotDePasse { get; set; } public string? Code { get; set; } }
public class ChangementMotDePasseDto { public string? Actuel { get; set; } public string? Nouveau { get; set; } }
