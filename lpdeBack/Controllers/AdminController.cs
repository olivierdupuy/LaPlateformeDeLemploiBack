using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Hubs;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<AppUser> _userManager;
    private readonly ActivityLogService _log;
    private readonly IHubContext<ChatHub> _hub;

    public AdminController(AppDbContext context, UserManager<AppUser> userManager, ActivityLogService log, IHubContext<ChatHub> hub)
    {
        _context = context;
        _userManager = userManager;
        _log = log;
        _hub = hub;
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string UserFullName() => $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}";
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    // Entreprises fictives créées par le seed de démonstration.
    private static readonly string[] SeedCompanies = { "TechCorp", "CreativeStudio", "CloudNine", "StartupFlow", "FinancePlus" };

    /// <summary>Admin : supprime les offres de démonstration seedées (candidatures liées supprimées en cascade).</summary>
    [HttpDelete("seed-offers")]
    public async Task<ActionResult<object>> DeleteSeedOffers()
    {
        var seed = await _context.JobOffers
            .Where(j => j.ExternalSource == null && SeedCompanies.Contains(j.Company))
            .ToListAsync();
        _context.JobOffers.RemoveRange(seed);
        await _context.SaveChangesAsync();
        return Ok(new { deleted = seed.Count, titles = seed.Select(o => o.Title), message = $"{seed.Count} offre(s) de démonstration supprimée(s)." });
    }

    /// <summary>
    /// Repartition des offres par provenance.
    ///
    /// Sert a decider avant de supprimer : une offre sans source externe
    /// n'est pas importee, elle a ete publiee sur la plateforme par un
    /// recruteur. Les deux ne se suppriment pas de la meme main.
    /// </summary>
    [HttpGet("offers/sources")]
    public async Task<ActionResult<object>> GetOfferSources()
    {
        var parSource = await _context.JobOffers
            .GroupBy(j => j.ExternalSource)
            .Select(g => new { source = g.Key, total = g.Count() })
            .OrderByDescending(x => x.total)
            .ToListAsync();

        return Ok(new
        {
            total = parSource.Sum(x => x.total),
            sources = parSource.Select(x => new
            {
                source = x.source ?? "(publiee sur la plateforme)",
                importee = x.source != null,
                x.total,
            }),
        });
    }

    /// <summary>
    /// Ne conserve que les offres France Travail parmi les offres importees.
    ///
    /// Les offres publiees sur la plateforme (sans source externe) sont
    /// preservees : ce sont celles des recruteurs, et les effacer
    /// detruirait leur travail. Passer preserverPlateforme a false les
    /// supprime aussi, ce qui doit rester un geste explicite.
    /// </summary>
    [HttpDelete("offers/keep-france-travail")]
    public async Task<ActionResult<object>> KeepOnlyFranceTravail([FromQuery] bool preserverPlateforme = true)
    {
        var aSupprimer = _context.JobOffers.Where(j => j.ExternalSource != "francetravail");
        if (preserverPlateforme)
            aSupprimer = aSupprimer.Where(j => j.ExternalSource != null);

        // Recapitulatif avant coupe : une suppression de masse doit
        // pouvoir se raconter apres coup.
        var detail = await aSupprimer
            .GroupBy(j => j.ExternalSource)
            .Select(g => new { source = g.Key ?? "(plateforme)", total = g.Count() })
            .ToListAsync();

        // ExecuteDelete : supprimer en base sans materialiser deux cent
        // mille entites. Les candidatures liees partent en cascade.
        var supprimees = await aSupprimer.ExecuteDeleteAsync();

        var restantes = await _context.JobOffers.CountAsync();
        await _log.Log("DeleteOffers", "JobOffer", null,
            $"{supprimees} offre(s) supprimee(s), {restantes} restante(s)",
            UserId(), UserFullName(), Ip());

        return Ok(new { supprimees, restantes, detail });
    }

    /// <summary>Admin : supprime toutes les offres d'une source d'import (ex. "adzuna") pour les ré-importer.</summary>
    [HttpDelete("offers-by-source/{source}")]
    public async Task<ActionResult<object>> DeleteBySource(string source)
    {
        var toDelete = await _context.JobOffers.Where(j => j.ExternalSource == source).ToListAsync();
        _context.JobOffers.RemoveRange(toDelete);
        await _context.SaveChangesAsync();
        return Ok(new { deleted = toDelete.Count, message = $"{toDelete.Count} offre(s) [{source}] supprimée(s)." });
    }

    // ═══════════════════════════════════
    //  1. JOURNAL D'ACTIVITE
    // ═══════════════════════════════════

    [HttpGet("activity-logs")]
    public async Task<ActionResult<object>> GetActivityLogs(
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _context.ActivityLogs.AsQueryable();

        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(userId)) query = query.Where(a => a.UserId == userId);

        var total = await query.CountAsync();
        var logs = await query.OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return new { logs, total, page, pageSize };
    }

    [HttpGet("activity-logs/actions")]
    public async Task<ActionResult<IEnumerable<string>>> GetLogActions()
    {
        return await _context.ActivityLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
    }

    // ═══════════════════════════════════
    //  2. MODERATION DES OFFRES
    // ═══════════════════════════════════

    [HttpGet("moderation")]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetModerationQueue([FromQuery] string? status)
    {
        var query = _context.JobOffers.Include(j => j.CreatedByUser).AsQueryable();
        // "all" sert l'explorateur d'offres du panneau, qui inspecte le
        // catalogue entier ; sans statut on garde la file d'attente, qui
        // reste l'usage par defaut de la moderation.
        if (string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        { /* aucun filtre */ }
        else if (!string.IsNullOrEmpty(status))
            query = query.Where(j => j.ModerationStatus == status);
        else
            query = query.Where(j => j.ModerationStatus == "Pending");

        return await query.OrderByDescending(j => j.CreatedAt).ToListAsync();
    }

    [HttpPatch("moderation/{id}/approve")]
    public async Task<IActionResult> ApproveOffer(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        job.ModerationStatus = "Approved";
        job.IsActive = true;
        await _context.SaveChangesAsync();

        await _log.Log("ApproveOffer", "JobOffer", id, $"Offre approuvee: {job.Title}", UserId(), UserFullName(), Ip());
        return Ok(new { job.Id, job.ModerationStatus });
    }

    [HttpPatch("moderation/{id}/reject")]
    public async Task<IActionResult> RejectOffer(int id, [FromBody] ModerationNoteDto dto)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        job.ModerationStatus = "Rejected";
        job.IsActive = false;
        job.ModerationNote = dto.Note;
        await _context.SaveChangesAsync();

        await _log.Log("RejectOffer", "JobOffer", id, $"Offre rejetee: {job.Title} — {dto.Note}", UserId(), UserFullName(), Ip());
        return Ok(new { job.Id, job.ModerationStatus });
    }

    [HttpPatch("moderation/{id}/feature")]
    public async Task<IActionResult> ToggleFeature(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        job.IsFeatured = !job.IsFeatured;
        await _context.SaveChangesAsync();

        await _log.Log("ToggleFeature", "JobOffer", id, $"Offre {(job.IsFeatured ? "mise en avant" : "retiree de la une")}: {job.Title}", UserId(), UserFullName(), Ip());
        return Ok(new { job.Id, job.IsFeatured });
    }

    // ═══════════════════════════════════
    //  3. ANNONCES & COMMUNICATION
    // ═══════════════════════════════════

    [HttpGet("announcements")]
    public async Task<ActionResult<IEnumerable<Announcement>>> GetAnnouncements()
    {
        return await _context.Announcements.OrderByDescending(a => a.CreatedAt).ToListAsync();
    }

    [HttpPost("announcements")]
    public async Task<ActionResult<Announcement>> CreateAnnouncement(AnnouncementCreateDto dto)
    {
        var ann = new Announcement
        {
            Title = dto.Title,
            Message = dto.Message,
            Type = dto.Type ?? "info",
            TargetRole = dto.TargetRole,
            IsBanner = dto.IsBanner,
            StartsAt = dto.StartsAt,
            EndsAt = dto.EndsAt,
            CreatedByUserId = UserId(),
        };

        _context.Announcements.Add(ann);
        await _context.SaveChangesAsync();

        // If it's a notification (not just a banner), send to users
        if (!dto.IsBanner)
        {
            var users = _userManager.Users.AsQueryable();
            if (!string.IsNullOrEmpty(dto.TargetRole))
                users = users.Where(u => u.Role == dto.TargetRole);

            var userList = await users.Select(u => u.Id).ToListAsync();
            foreach (var uid in userList)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = uid,
                    Title = dto.Title,
                    Message = dto.Message,
                    Type = "Annonce",
                    Link = "/",
                });
            }
            await _context.SaveChangesAsync();

            // Push via SignalR
            foreach (var uid in userList)
            {
                foreach (var connId in ChatHub.GetConnectionIds(uid))
                    await _hub.Clients.Client(connId).SendAsync("NewNotification");
            }
        }

        await _log.Log("CreateAnnouncement", "Announcement", ann.Id, $"Annonce creee: {ann.Title}", UserId(), UserFullName(), Ip());
        return CreatedAtAction(nameof(GetAnnouncements), new { id = ann.Id }, ann);
    }

    [HttpDelete("announcements/{id}")]
    public async Task<IActionResult> DeleteAnnouncement(int id)
    {
        var ann = await _context.Announcements.FindAsync(id);
        if (ann == null) return NotFound();
        _context.Announcements.Remove(ann);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("announcements/{id}/toggle")]
    public async Task<IActionResult> ToggleAnnouncement(int id)
    {
        var ann = await _context.Announcements.FindAsync(id);
        if (ann == null) return NotFound();
        ann.IsActive = !ann.IsActive;
        await _context.SaveChangesAsync();
        return Ok(new { ann.Id, ann.IsActive });
    }

    // Public endpoint — active banners (no auth required)
    [HttpGet("banners")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<object>>> GetActiveBanners()
    {
        var now = DateTime.UtcNow;
        var banners = await _context.Announcements
            .Where(a => a.IsActive && a.IsBanner &&
                        (a.StartsAt == null || a.StartsAt <= now) &&
                        (a.EndsAt == null || a.EndsAt >= now))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.Id, a.Title, a.Message, a.Type })
            .ToListAsync();
        return Ok(banners);
    }

    // ═══════════════════════════════════
    //  4. EXPORT CSV
    // ═══════════════════════════════════

    [HttpGet("export/users")]
    public async Task<IActionResult> ExportUsers()
    {
        var users = await _userManager.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
        var csv = new StringBuilder();
        csv.AppendLine("Id,Prenom,Nom,Email,Role,Entreprise,Ville,Inscription,Actif,En ligne");
        foreach (var u in users)
            csv.AppendLine($"\"{u.Id}\",\"{u.FirstName}\",\"{u.LastName}\",\"{u.Email}\",\"{u.Role}\",\"{u.Company}\",\"{u.City}\",\"{u.CreatedAt:yyyy-MM-dd}\",\"{u.IsActive}\",\"{ChatHub.IsUserOnline(u.Id)}\"");

        await _log.Log("ExportCSV", "User", null, $"Export {users.Count} utilisateurs", UserId(), UserFullName(), Ip());
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"utilisateurs_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("export/offers")]
    public async Task<IActionResult> ExportOffers()
    {
        var offers = await _context.JobOffers.Include(j => j.Applications).OrderByDescending(j => j.CreatedAt).ToListAsync();
        var csv = new StringBuilder();
        csv.AppendLine("Id,Titre,Entreprise,Ville,Categorie,Contrat,Remote,SalaireMin,SalaireMax,Vues,Candidatures,Active,Creation,Expiration,Moderation");
        foreach (var j in offers)
            csv.AppendLine($"{j.Id},\"{j.Title}\",\"{j.Company}\",\"{j.Location}\",\"{j.Category}\",\"{j.ContractType}\",{j.IsRemote},{j.MinSalary},{j.MaxSalary},{j.ViewCount},{j.Applications.Count},{j.IsActive},\"{j.CreatedAt:yyyy-MM-dd}\",\"{j.ExpiresAt:yyyy-MM-dd}\",\"{j.ModerationStatus}\"");

        await _log.Log("ExportCSV", "JobOffer", null, $"Export {offers.Count} offres", UserId(), UserFullName(), Ip());
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"offres_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("export/applications")]
    public async Task<IActionResult> ExportApplications()
    {
        var apps = await _context.Applications.Include(a => a.JobOffer).OrderByDescending(a => a.AppliedAt).ToListAsync();
        var csv = new StringBuilder();
        csv.AppendLine("Id,Candidat,Email,Offre,Entreprise,Statut,Source,DateCandidature");
        foreach (var a in apps)
            csv.AppendLine($"{a.Id},\"{a.FullName}\",\"{a.Email}\",\"{a.JobOffer?.Title}\",\"{a.JobOffer?.Company}\",\"{a.Status}\",\"{a.Source}\",\"{a.AppliedAt:yyyy-MM-dd}\"");

        await _log.Log("ExportCSV", "Application", null, $"Export {apps.Count} candidatures", UserId(), UserFullName(), Ip());
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"candidatures_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ═══════════════════════════════════
    //  PRISE EN MAIN DE COMPTE
    //
    //  Un administrateur peut agir sous l'identite d'un candidat ou d'un
    //  recruteur, pour reproduire un probleme qu'on lui decrit. C'est la
    //  fonction la plus sensible du panneau : elle donne acces a des
    //  messages prives et permet d'agir au nom de quelqu'un.
    //
    //  Quatre garde-fous, tous portes par le jeton lui-meme :
    //   - il nomme l'administrateur reel autant que le compte emprunte,
    //     pour que le journal dise qui a agi et pas seulement sous quel
    //     compte ;
    //   - un administrateur ne peut pas en emprunter un autre, sans quoi
    //     la separation des roles ne vaudrait plus rien ;
    //   - il vit trente minutes : un emprunt n'est pas un mode de travail ;
    //   - l'entree et la sortie laissent une trace.
    // ═══════════════════════════════════

    [HttpPost("users/{id}/impersonate")]
    public async Task<ActionResult<object>> Impersonate(string id,
        [FromServices] IConfiguration config, CancellationToken ct)
    {
        var cible = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (cible == null) return NotFound();

        if (cible.Role == "Admin")
            return BadRequest(new { message = "Un administrateur ne peut pas etre emprunte." });

        if (!cible.IsActive)
            return BadRequest(new { message = "Ce compte est suspendu." });

        var adminId = UserId();
        if (cible.Id == adminId)
            return BadRequest(new { message = "Vous etes deja sur votre propre compte." });

        var jeton = ConstruireJetonEmprunt(cible, adminId, UserFullName(), config);

        await _log.Log("ImpersonateStart", "User", null,
            $"Prise en main du compte {cible.Email} ({cible.Role})",
            adminId, UserFullName(), Ip());

        return Ok(new
        {
            token = jeton,
            expiration = DateTime.UtcNow.AddMinutes(DureeEmpruntMinutes),
            user = new
            {
                cible.Id, cible.Email, cible.FirstName, cible.LastName, cible.Role,
                cible.AvatarUrl, cible.Company, cible.City, cible.Title,
            },
            emprunt = new { parId = adminId, parNom = UserFullName() },
        });
    }

    /// <summary>
    /// Fin de l'emprunt. L'appel se fait avec le jeton d'emprunt : c'est
    /// lui qui porte l'identite de l'administrateur a qui rendre la main.
    /// </summary>
    /// <remarks>
    /// AllowAnonymous leve la contrainte de role posee sur le controleur :
    /// pendant un emprunt, le jeton porte le role du compte emprunte, pas
    /// « Admin ». Sans cela, on ne pourrait plus rendre la main — on
    /// restait prisonnier du compte jusqu'a expiration.
    ///
    /// La route n'est pas ouverte pour autant : elle exige la revendication
    /// « impersonator_id », que seul un jeton d'emprunt signe par le
    /// serveur peut porter.
    /// </remarks>
    [HttpPost("impersonate/stop")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> StopImpersonation(
        [FromServices] UserManager<AppUser> users,
        [FromServices] IConfiguration config, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("impersonator_id");
        if (string.IsNullOrEmpty(adminId))
            return BadRequest(new { message = "Cette session n'est pas un emprunt." });

        var admin = await _context.Users.FirstOrDefaultAsync(u => u.Id == adminId, ct);
        if (admin == null || admin.Role != "Admin")
            return Unauthorized(new { message = "Administrateur d'origine introuvable." });

        var emprunte = User.FindFirstValue(ClaimTypes.Email);
        await _log.Log("ImpersonateStop", "User", null,
            $"Fin de la prise en main du compte {emprunte}",
            admin.Id, $"{admin.FirstName} {admin.LastName}", Ip());

        return Ok(new
        {
            token = ConstruireJetonStandard(admin, config),
            user = new
            {
                admin.Id, admin.Email, admin.FirstName, admin.LastName, admin.Role,
                admin.AvatarUrl, admin.Company, admin.City, admin.Title,
            },
        });
    }

    private const int DureeEmpruntMinutes = 30;

    private static string ConstruireJetonEmprunt(AppUser cible, string adminId,
                                                 string adminNom, IConfiguration config)
    {
        var claims = ClaimsDeBase(cible);
        // Les deux identites voyagent ensemble : c'est ce qui permet au
        // journal de nommer l'auteur reel d'une action.
        claims.Add(new Claim("impersonator_id", adminId));
        claims.Add(new Claim("impersonator_name", adminNom));
        return Signer(claims, config, DateTime.UtcNow.AddMinutes(DureeEmpruntMinutes));
    }

    private static string ConstruireJetonStandard(AppUser u, IConfiguration config)
        => Signer(ClaimsDeBase(u), config, DateTime.UtcNow.AddHours(8));

    private static List<Claim> ClaimsDeBase(AppUser u) => new()
    {
        new Claim(ClaimTypes.NameIdentifier, u.Id),
        new Claim(ClaimTypes.Email, u.Email ?? string.Empty),
        new Claim(ClaimTypes.GivenName, u.FirstName ?? string.Empty),
        new Claim(ClaimTypes.Surname, u.LastName ?? string.Empty),
        new Claim(ClaimTypes.Role, u.Role),
    };

    private static string Signer(IEnumerable<Claim> claims, IConfiguration config, DateTime expire)
    {
        var cle = config["Jwt:Key"] ?? "CleParDefautDeDeveloppementUniquement_32Caracteres!";
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cle)),
            SecurityAlgorithms.HmacSha256);

        var jeton = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"] ?? "LpdeBack",
            audience: config["Jwt:Audience"] ?? "LpdeFront",
            claims: claims,
            expires: expire,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jeton);
    }

    // ═══════════════════════════════════
    //  4 ter. EXPLORATEURS PAGINES
    //
    //  Les listes du panneau se comptent en milliers de lignes. Tout
    //  renvoyer pour filtrer dans le navigateur fait payer a chaque
    //  ouverture le catalogue entier ; le tri, le filtre et la decoupe se
    //  font donc ici, et la reponse ne porte que la page demandee.
    //
    //  Les facettes (les compteurs de l'en-tete) sont calculees sur
    //  l'ensemble filtre AVANT la pagination : sinon elles ne compteraient
    //  que la page affichee.
    // ═══════════════════════════════════

    private static (int page, int size) Paging(int page, int pageSize)
        => (page < 1 ? 1 : page, pageSize is < 1 or > 100 ? 25 : pageSize);

    [HttpGet("offers")]
    public async Task<ActionResult<object>> GetOffers(
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromQuery] string? contractType,
        [FromQuery] string? company,
        [FromQuery] string? status,
        [FromQuery] string? experience,
        [FromQuery] string? location,
        [FromQuery] bool? remote,
        [FromQuery] DateTime? day,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        (page, pageSize) = Paging(page, pageSize);

        var query = _context.JobOffers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(j => j.Title.Contains(q) || j.Company.Contains(q) || j.Location.Contains(q));
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(j => j.Category == category);
        if (!string.IsNullOrWhiteSpace(contractType)) query = query.Where(j => j.ContractType == contractType);
        if (!string.IsNullOrWhiteSpace(company)) query = query.Where(j => j.Company == company);
        if (!string.IsNullOrWhiteSpace(experience)) query = query.Where(j => j.ExperienceRequired == experience);
        if (!string.IsNullOrWhiteSpace(location)) query = query.Where(j => j.Location.Contains(location));
        if (remote == true) query = query.Where(j => j.IsRemote);
        if (!string.IsNullOrWhiteSpace(status))
        {
            // Une offre sans statut est en attente : le filtre doit la voir.
            if (status == "Pending")
                query = query.Where(j => j.ModerationStatus == "Pending" || j.ModerationStatus == null);
            else
                query = query.Where(j => j.ModerationStatus == status);
        }
        if (day.HasValue)
        {
            var from = day.Value.Date;
            var to = from.AddDays(1);
            query = query.Where(j => j.CreatedAt >= from && j.CreatedAt < to);
        }

        var facets = await query.GroupBy(_ => 1).Select(g => new OfferFacetsDto
        {
            Total = g.Count(),
            Approved = g.Count(j => j.ModerationStatus == "Approved"),
            Pending = g.Count(j => j.ModerationStatus == "Pending" || j.ModerationStatus == null),
            Rejected = g.Count(j => j.ModerationStatus == "Rejected"),
            Remote = g.Count(j => j.IsRemote),
            Views = g.Sum(j => j.ViewCount),
        }).FirstOrDefaultAsync() ?? new OfferFacetsDto();

        query = sort switch
        {
            "views" => query.OrderByDescending(j => j.ViewCount),
            "title" => query.OrderBy(j => j.Title),
            "company" => query.OrderBy(j => j.Company).ThenBy(j => j.Title),
            _ => query.OrderByDescending(j => j.CreatedAt),
        };

        // La description pese plusieurs kilo-octets et ne s'affiche pas
        // dans le tableau : la projection evite de la transporter.
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(j => new
            {
                j.Id, j.Title, j.Company, j.Location, j.Category, j.ContractType,
                j.IsRemote, j.CreatedAt, j.ViewCount, j.ModerationStatus,
                j.IsFeatured, j.IsUrgent, j.ExperienceRequired,
            })
            .ToListAsync();

        return new { items, total = facets.Total, page, pageSize, facets };
    }

    [HttpGet("applications")]
    public async Task<ActionResult<object>> GetApplications(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] int? offerId,
        [FromQuery] string? company,
        [FromQuery] string? source,
        [FromQuery] DateTime? day,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        (page, pageSize) = Paging(page, pageSize);

        var query = _context.Applications.Include(a => a.JobOffer).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.FullName.Contains(q) || a.Email.Contains(q)
                                  || a.JobOffer!.Title.Contains(q) || a.JobOffer.Company.Contains(q));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (offerId.HasValue) query = query.Where(a => a.JobOfferId == offerId.Value);
        if (!string.IsNullOrWhiteSpace(company)) query = query.Where(a => a.JobOffer!.Company == company);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(a => a.Source == source);
        if (day.HasValue)
        {
            var from = day.Value.Date;
            var to = from.AddDays(1);
            query = query.Where(a => a.AppliedAt >= from && a.AppliedAt < to);
        }

        var facets = await query.GroupBy(_ => 1).Select(g => new ApplicationFacetsDto
        {
            Total = g.Count(),
            Pending = g.Count(a => a.Status == "Pending"),
            Reviewed = g.Count(a => a.Status == "Reviewed"),
            Accepted = g.Count(a => a.Status == "Accepted"),
            Rejected = g.Count(a => a.Status == "Rejected"),
        }).FirstOrDefaultAsync() ?? new ApplicationFacetsDto();

        query = sort switch
        {
            "candidate" => query.OrderBy(a => a.FullName),
            "company" => query.OrderBy(a => a.JobOffer!.Company),
            "status" => query.OrderBy(a => a.Status),
            _ => query.OrderByDescending(a => a.AppliedAt),
        };

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.JobOfferId, a.FullName, a.Email, a.Status, a.AppliedAt,
                a.IsArchived, a.ReviewedAt, a.ResumeUrl, a.Source,
                jobTitle = a.JobOffer!.Title, company = a.JobOffer.Company,
            })
            .ToListAsync();

        return new { items, total = facets.Total, page, pageSize, facets };
    }

    [HttpGet("interviews")]
    public async Task<ActionResult<object>> GetInterviews(
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] string? company,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        (page, pageSize) = Paging(page, pageSize);

        var query = _context.Interviews
            .Include(i => i.Application).ThenInclude(a => a.JobOffer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(i => i.Application.FullName.Contains(q)
                                  || i.Application.JobOffer!.Title.Contains(q)
                                  || i.Application.JobOffer.Company.Contains(q));
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(i => i.Type == type);
        if (!string.IsNullOrWhiteSpace(company)) query = query.Where(i => i.Application.JobOffer!.Company == company);

        var now = DateTime.UtcNow;
        var facets = await query.GroupBy(_ => 1).Select(g => new InterviewFacetsDto
        {
            Total = g.Count(),
            Proposed = g.Count(i => i.Status == "Proposed"),
            Accepted = g.Count(i => i.Status == "Accepted"),
            Completed = g.Count(i => i.Status == "Completed"),
            Cancelled = g.Count(i => i.Status == "Cancelled"),
            Upcoming = g.Count(i => i.ProposedAt > now && i.Status != "Cancelled"),
        }).FirstOrDefaultAsync() ?? new InterviewFacetsDto();

        query = sort switch
        {
            "candidate" => query.OrderBy(i => i.Application.FullName),
            "company" => query.OrderBy(i => i.Application.JobOffer!.Company),
            "status" => query.OrderBy(i => i.Status),
            _ => query.OrderBy(i => i.ProposedAt),
        };

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new
            {
                i.Id, i.ApplicationId, i.ProposedAt, i.Status, i.Type, i.Duration,
                i.Location, i.InterviewerName,
                candidateName = i.Application.FullName,
                jobTitle = i.Application.JobOffer!.Title,
                company = i.Application.JobOffer.Company,
            })
            .ToListAsync();

        return new { items, total = facets.Total, page, pageSize, facets };
    }

    [HttpGet("users")]
    public async Task<ActionResult<object>> GetUsers(
        [FromQuery] string? q,
        [FromQuery] string? role,
        [FromQuery] DateTime? day,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        (page, pageSize) = Paging(page, pageSize);

        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(u => u.FirstName.Contains(q) || u.LastName.Contains(q)
                                  || u.Email!.Contains(q) || (u.Company != null && u.Company.Contains(q)));
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(u => u.Role == role);
        if (day.HasValue)
        {
            var from = day.Value.Date;
            var to = from.AddDays(1);
            query = query.Where(u => u.CreatedAt >= from && u.CreatedAt < to);
        }

        var facets = await query.GroupBy(_ => 1).Select(g => new UserFacetsDto
        {
            Total = g.Count(),
            Admins = g.Count(u => u.Role == "Admin"),
            Recruiters = g.Count(u => u.Role == "Recruiter"),
            Candidates = g.Count(u => u.Role == "Candidate"),
            Suspended = g.Count(u => !u.IsActive),
        }).FirstOrDefaultAsync() ?? new UserFacetsDto();

        query = sort switch
        {
            "name" => query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName),
            "role" => query.OrderBy(u => u.Role).ThenBy(u => u.LastName),
            _ => query.OrderByDescending(u => u.CreatedAt),
        };

        var slice = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        // La presence est tenue en memoire par le hub : elle ne peut pas
        // etre jointe en base, on l'ajoute sur la page servie.
        var items = slice.Select(u => new
        {
            u.Id, u.Email, u.FirstName, u.LastName, u.Role, u.Company,
            u.AvatarUrl, u.City, u.Title, u.CreatedAt, u.IsActive, u.IsSearchable,
            isOnline = ChatHub.IsUserOnline(u.Id),
        });

        facets.Online = ChatHub.GetOnlineUserIds().Count();

        return new { items, total = facets.Total, page, pageSize, facets };
    }

    // ═══════════════════════════════════
    //  PIECES DU DOSSIER
    //
    //  Les onglets du dossier ne sont pas des formulaires mais des listes.
    //  L'enregistrement se fait donc par ligne, au geste, plutot que par
    //  un bouton global qui laisserait croire a un etat d'ensemble a
    //  valider.
    //
    //  Le journal d'activite n'a volontairement aucune route d'ecriture :
    //  une trace d'audit modifiable ne prouve plus rien, et il enregistre
    //  desormais les prises en main de compte.
    // ═══════════════════════════════════

    [HttpPatch("applications/{id}")]
    public async Task<IActionResult> ModifierCandidature(int id, [FromBody] PieceDossierDto dto)
    {
        var a = await _context.Applications.Include(x => x.JobOffer).FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Statut))
        {
            var valides = new[] { "Pending", "Reviewed", "Accepted", "Rejected" };
            if (!valides.Contains(dto.Statut)) return BadRequest(new { message = "Statut inconnu." });

            // Premiere transition hors « en attente » : on horodate la
            // consultation, comme le fait le recruteur.
            if (a.Status == "Pending" && dto.Statut != "Pending" && a.ReviewedAt == null)
                a.ReviewedAt = DateTime.UtcNow;
            a.Status = dto.Statut;
        }

        if (dto.Archivee.HasValue) a.IsArchived = dto.Archivee.Value;
        if (dto.Notes != null) a.RecruiterNotes = dto.Notes;

        await _context.SaveChangesAsync();
        await _log.Log("UpdateApplication", "Application", id,
            $"Candidature de {a.FullName} modifiee", UserId(), UserFullName(), Ip());
        return Ok(new { a.Id, a.Status, a.IsArchived, a.ReviewedAt });
    }

    [HttpDelete("applications/{id}")]
    public async Task<IActionResult> SupprimerCandidature(int id)
    {
        var a = await _context.Applications.FindAsync(id);
        if (a == null) return NotFound();
        _context.Applications.Remove(a);
        await _context.SaveChangesAsync();
        await _log.Log("DeleteApplication", "Application", id,
            $"Candidature de {a.FullName} supprimee", UserId(), UserFullName(), Ip());
        return NoContent();
    }

    [HttpPatch("saved-searches/{id}")]
    public async Task<IActionResult> ModifierRecherche(int id, [FromBody] PieceDossierDto dto)
    {
        var s = await _context.SavedSearches.FindAsync(id);
        if (s == null) return NotFound();
        if (dto.AlerteActive.HasValue) s.AlertEnabled = dto.AlerteActive.Value;
        if (dto.Libelle != null) s.Label = dto.Libelle;
        await _context.SaveChangesAsync();
        return Ok(new { s.Id, s.AlertEnabled, s.Label });
    }

    [HttpDelete("saved-searches/{id}")]
    public async Task<IActionResult> SupprimerRecherche(int id)
    {
        var s = await _context.SavedSearches.FindAsync(id);
        if (s == null) return NotFound();
        _context.SavedSearches.Remove(s);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("interviews/{id}")]
    public async Task<IActionResult> ModifierEntretien(int id, [FromBody] PieceDossierDto dto)
    {
        var i = await _context.Interviews.FindAsync(id);
        if (i == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(dto.Statut))
        {
            var valides = new[] { "Proposed", "Accepted", "Completed", "Cancelled" };
            if (!valides.Contains(dto.Statut)) return BadRequest(new { message = "Statut inconnu." });
            i.Status = dto.Statut;
        }
        if (dto.Notes != null) i.Notes = dto.Notes;
        await _context.SaveChangesAsync();
        await _log.Log("UpdateInterview", "Interview", id,
            "Entretien modifie", UserId(), UserFullName(), Ip());
        return Ok(new { i.Id, i.Status });
    }

    [HttpDelete("interviews/{id}")]
    public async Task<IActionResult> SupprimerEntretien(int id)
    {
        var i = await _context.Interviews.FindAsync(id);
        if (i == null) return NotFound();
        _context.Interviews.Remove(i);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("job-notes/{id}")]
    public async Task<IActionResult> SupprimerNote(int id)
    {
        var n = await _context.JobNotes.FindAsync(id);
        if (n == null) return NotFound();
        _context.JobNotes.Remove(n);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("cv-sections/{id}")]
    public async Task<IActionResult> ModifierSectionCv(int id, [FromBody] SectionCvDto dto)
    {
        var c = await _context.CvSections.FindAsync(id);
        if (c == null) return NotFound();
        if (dto.Titre != null) c.Title = dto.Titre;
        if (dto.Organisation != null) c.Organization = dto.Organisation;
        if (dto.Lieu != null) c.Location = dto.Lieu;
        if (dto.Description != null) c.Description = dto.Description;
        if (dto.Niveau != null) c.Level = dto.Niveau;
        await _context.SaveChangesAsync();
        return Ok(new { c.Id, c.Title });
    }

    [HttpDelete("cv-sections/{id}")]
    public async Task<IActionResult> SupprimerSectionCv(int id)
    {
        var c = await _context.CvSections.FindAsync(id);
        if (c == null) return NotFound();
        _context.CvSections.Remove(c);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══════════════════════════════════
    //  4 bis. DOSSIER D'UN UTILISATEUR
    // ═══════════════════════════════════

    /// <summary>
    /// Tout ce que la plateforme sait d'un compte, en une seule reponse.
    /// La fiche du panneau affiche sept onglets ; les servir par sept
    /// appels ferait sept allers-retours pour une page qu'on ouvre d'un
    /// clic depuis un tableau.
    /// </summary>
    [HttpGet("users/{id}/dossier")]
    public async Task<ActionResult<object>> GetUserDossier(string id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        var applications = await _context.Applications
            .Include(a => a.JobOffer)
            .Where(a => a.UserId == id)
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new
            {
                a.Id, a.JobOfferId, a.Status, a.AppliedAt, a.IsArchived, a.ReviewedAt,
                a.CoverLetter, a.ResumeUrl, a.Source, a.SalaryExpectation, a.AvailableFrom,
                jobTitle = a.JobOffer!.Title, company = a.JobOffer.Company,
                location = a.JobOffer.Location, contractType = a.JobOffer.ContractType,
            })
            .ToListAsync();

        var savedSearches = await _context.SavedSearches
            .Where(s => s.UserId == id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var interviews = await _context.Interviews
            .Include(i => i.Application).ThenInclude(a => a.JobOffer)
            .Where(i => i.Application.UserId == id)
            .OrderByDescending(i => i.ProposedAt)
            .Select(i => new
            {
                i.Id, i.ApplicationId, i.ProposedAt, i.Status, i.Type, i.Duration,
                i.Location, i.InterviewerName, i.Notes,
                jobTitle = i.Application.JobOffer!.Title, company = i.Application.JobOffer.Company,
            })
            .ToListAsync();

        var cvSections = await _context.CvSections
            .Where(c => c.UserId == id)
            .OrderBy(c => c.SectionType).ThenBy(c => c.SortOrder)
            .ToListAsync();

        var notes = await _context.JobNotes
            .Include(n => n.JobOffer)
            .Where(n => n.UserId == id)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => new
            {
                n.Id, n.JobOfferId, n.Content, n.UpdatedAt,
                jobTitle = n.JobOffer!.Title, company = n.JobOffer.Company,
            })
            .ToListAsync();

        var activity = await _context.ActivityLogs
            .Where(l => l.UserId == id)
            .OrderByDescending(l => l.CreatedAt)
            .Take(50)
            .ToListAsync();

        return new
        {
            user = new
            {
                user.Id, user.Email, user.FirstName, user.LastName, user.Role,
                user.AvatarUrl, user.Company, user.Bio, user.ResumeUrl, user.Title,
                user.Skills, user.ExperienceYears, user.Education, user.City,
                user.LinkedInUrl, user.PortfolioUrl, user.IsSearchable, user.IsActive,
                user.CreatedAt, user.PhoneNumber, user.EmailConfirmed,
            },
            applications,
            savedSearches,
            interviews,
            cvSections,
            notes,
            activity,
        };
    }

    /// <summary>
    /// Edition d'un compte par l'administration. Seuls les champs fournis
    /// sont ecrits : un formulaire qui n'affiche pas un champ ne doit pas
    /// l'effacer au passage.
    /// </summary>
    [HttpPut("users/{id}")]
    public async Task<ActionResult<object>> UpdateUser(string id, [FromBody] AdminUserUpdateDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        if (dto.FirstName != null) user.FirstName = dto.FirstName;
        if (dto.LastName != null) user.LastName = dto.LastName;
        if (dto.Title != null) user.Title = dto.Title;
        if (dto.Bio != null) user.Bio = dto.Bio;
        if (dto.Skills != null) user.Skills = dto.Skills;
        if (dto.Education != null) user.Education = dto.Education;
        if (dto.City != null) user.City = dto.City;
        if (dto.Company != null) user.Company = dto.Company;
        if (dto.LinkedInUrl != null) user.LinkedInUrl = dto.LinkedInUrl;
        if (dto.PortfolioUrl != null) user.PortfolioUrl = dto.PortfolioUrl;
        if (dto.AvatarUrl != null) user.AvatarUrl = dto.AvatarUrl;
        if (dto.ResumeUrl != null) user.ResumeUrl = dto.ResumeUrl;
        if (dto.ExperienceYears.HasValue) user.ExperienceYears = dto.ExperienceYears;
        if (dto.IsSearchable.HasValue) user.IsSearchable = dto.IsSearchable.Value;
        if (dto.IsActive.HasValue) user.IsActive = dto.IsActive.Value;
        if (dto.PhoneNumber != null) user.PhoneNumber = dto.PhoneNumber;

        // Le courriel sert d'identifiant de connexion : les deux colonnes
        // d'Identity doivent bouger ensemble, sinon le compte ne se
        // connecte plus.
        if (!string.IsNullOrWhiteSpace(dto.Email) && dto.Email != user.Email)
        {
            var taken = await _context.Users.AnyAsync(u => u.Id != id && u.NormalizedEmail == dto.Email.ToUpperInvariant());
            if (taken) return Conflict(new { message = "Cette adresse est deja utilisee." });
            user.Email = dto.Email;
            user.NormalizedEmail = dto.Email.ToUpperInvariant();
            user.UserName = dto.Email;
            user.NormalizedUserName = dto.Email.ToUpperInvariant();
        }

        await _context.SaveChangesAsync();
        await _log.Log("UpdateUser", "User", null,
            $"Profil de {user.Email} modifie", UserId(), UserFullName(), Ip());

        return Ok(new { message = "Profil mis a jour" });
    }

    /// <summary>
    /// Reinitialisation du mot de passe par l'administration : sans jeton
    /// de recuperation, le compte serait perdu si la personne n'a plus
    /// acces a sa boite.
    /// </summary>
    [HttpPost("users/{id}/password")]
    public async Task<IActionResult> SetUserPassword(string id, [FromBody] AdminPasswordDto dto)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });

        await _log.Log("ResetPassword", "User", null,
            $"Mot de passe de {user.Email} reinitialise", UserId(), UserFullName(), Ip());

        return Ok(new { message = "Mot de passe reinitialise" });
    }

    // ═══════════════════════════════════
    //  5. PARAMETRES PLATEFORME
    // ═══════════════════════════════════

    [HttpGet("settings")]
    public async Task<ActionResult<IEnumerable<PlatformSetting>>> GetSettings()
    {
        return await _context.PlatformSettings.OrderBy(s => s.Key).ToListAsync();
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(List<SettingUpdateDto> settings)
    {
        foreach (var dto in settings)
        {
            var setting = await _context.PlatformSettings.FirstOrDefaultAsync(s => s.Key == dto.Key);
            if (setting != null)
            {
                setting.Value = dto.Value;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.PlatformSettings.Add(new PlatformSetting
                {
                    Key = dto.Key,
                    Value = dto.Value,
                    Type = dto.Type ?? "string",
                    Description = dto.Description ?? "",
                });
            }
        }
        await _context.SaveChangesAsync();

        await _log.Log("UpdateSettings", "PlatformSetting", null, $"Parametres mis a jour ({settings.Count} modif.)", UserId(), UserFullName(), Ip());
        return NoContent();
    }

    [HttpGet("settings/{key}")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> GetSetting(string key)
    {
        var setting = await _context.PlatformSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null) return NotFound();
        return new { setting.Key, setting.Value, setting.Type };
    }

    /// <summary>Public settings for frontend (no auth needed)</summary>
    [HttpGet("public-settings")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> GetPublicSettings()
    {
        var publicKeys = new[] { "welcome_message", "contact_email", "allow_registration", "require_moderation", "maintenance_mode", "max_applications_per_candidate" };
        var settings = await _context.PlatformSettings
            .Where(s => publicKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(settings);
    }
}

// ── DTOs ──
public class ModerationNoteDto { public string? Note { get; set; } }

/// <summary>
/// Edition d'un compte par l'administration. Tout est optionnel : null
/// signifie « ne touche pas a ce champ », pas « efface-le ».
/// </summary>
public class AdminUserUpdateDto
{
    [MaxLength(100)] public string? FirstName { get; set; }
    [MaxLength(100)] public string? LastName { get; set; }
    [EmailAddress, MaxLength(256)] public string? Email { get; set; }
    [MaxLength(30)] public string? PhoneNumber { get; set; }
    [MaxLength(150)] public string? Title { get; set; }
    [MaxLength(500)] public string? Bio { get; set; }
    [MaxLength(500)] public string? Skills { get; set; }
    [MaxLength(200)] public string? Education { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(200)] public string? Company { get; set; }
    [MaxLength(300)] public string? LinkedInUrl { get; set; }
    [MaxLength(300)] public string? PortfolioUrl { get; set; }
    [MaxLength(500)] public string? AvatarUrl { get; set; }
    [MaxLength(500)] public string? ResumeUrl { get; set; }
    public int? ExperienceYears { get; set; }
    public bool? IsSearchable { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminPasswordDto
{
    [Required, MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// Modification d'une piece du dossier. Tout est optionnel : null
/// signifie « ne touche pas », pas « efface ».
/// </summary>
public class PieceDossierDto
{
    [MaxLength(30)] public string? Statut { get; set; }
    public bool? Archivee { get; set; }
    public bool? AlerteActive { get; set; }
    [MaxLength(200)] public string? Libelle { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
}

public class SectionCvDto
{
    [MaxLength(200)] public string? Titre { get; set; }
    [MaxLength(200)] public string? Organisation { get; set; }
    [MaxLength(200)] public string? Lieu { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(100)] public string? Niveau { get; set; }
}

// Les facettes alimentent les compteurs de l'en-tete des explorateurs.
// Elles portent sur l'ensemble filtre, pas sur la page servie.
public class OfferFacetsDto
{
    public int Total { get; set; }
    public int Approved { get; set; }
    public int Pending { get; set; }
    public int Rejected { get; set; }
    public int Remote { get; set; }
    public int Views { get; set; }
}

public class ApplicationFacetsDto
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Reviewed { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
}

public class InterviewFacetsDto
{
    public int Total { get; set; }
    public int Proposed { get; set; }
    public int Accepted { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Upcoming { get; set; }
}

public class UserFacetsDto
{
    public int Total { get; set; }
    public int Admins { get; set; }
    public int Recruiters { get; set; }
    public int Candidates { get; set; }
    public int Suspended { get; set; }
    public int Online { get; set; }
}

public class AnnouncementCreateDto
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? TargetRole { get; set; }
    public bool IsBanner { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
}

public class SettingUpdateDto
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Description { get; set; }
}
