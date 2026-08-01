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
using lpdeBack.Validation;

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

    /// <summary>
    /// Le membre dont on touche le dossier.
    ///
    /// « Section de CV 412 supprimée » n'apprend rien a qui relira le
    /// journal dans six mois. Ce qui fait le sens de l'entree, c'est le
    /// compte concerne : un administrateur qui modifie le CV de
    /// quelqu'un d'autre est exactement ce qu'une trace doit montrer.
    /// </summary>
    private async Task<string> Concerne(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return "compte inconnu";
        var u = await _context.Users
            .Where(x => x.Id == userId)
            .Select(x => new { x.FirstName, x.LastName, x.Email })
            .FirstOrDefaultAsync();
        return u == null ? "compte supprime" : $"{u.FirstName} {u.LastName} ({u.Email})".Trim();
    }

    // Entreprises fictives créées par le seed de démonstration.
    private static readonly string[] SeedCompanies = { "TechCorp", "CreativeStudio", "CloudNine", "StartupFlow", "FinancePlus" };

    // Comptes fictifs du meme seed. Leurs mots de passe figurent dans Program.cs,
    // sur un depot public : tant qu'un de ces comptes repond en production, ses
    // acces sont ouverts a quiconque lit le code.
    //
    // Toute persona ajoutee au seed doit etre reportee ici, sinon l'inventaire la
    // manquera.
    private static readonly string[] SeedAccountEmails =
    {
        "admin@lpde.fr",
        "sophie.martin@techcorp.fr", "lucas.bernard@creativestudio.fr", "emma.dubois@cloudnine.fr",
        "thomas.petit@startupflow.fr", "marie.leroy@financeplus.fr",
        "jean.dupont@email.fr", "alice.moreau@email.fr", "karim.benali@email.fr",
        "camille.roux@email.fr", "hugo.lambert@email.fr",
    };

    private record SeedAccountReport(
        string email, string role, DateTime createdAt,
        int offersCreated, int applicationsReceived, int realApplicationsReceived, int applicationsSent);

    /// <summary>
    /// Admin : inventaire des comptes de démonstration encore présents et de ce qui
    /// leur est rattaché. Lecture seule, ne modifie rien.
    ///
    /// Sert à décider avant de supprimer : un recruteur fictif peut porter des
    /// offres sur lesquelles de vraies personnes ont postulé. Ces candidatures-là
    /// sont le seul élément qui compte dans l'arbitrage — la cascade les emporterait
    /// avec l'offre.
    /// </summary>
    [HttpGet("seed-accounts")]
    public async Task<ActionResult<object>> GetSeedAccounts()
    {
        var users = await _context.Users
            .Where(u => u.Email != null && SeedAccountEmails.Contains(u.Email))
            .Select(u => new { u.Id, u.Email, u.Role, u.CreatedAt })
            .ToListAsync();

        var seedUserIds = users.Select(u => u.Id).ToList();
        var report = new List<SeedAccountReport>();

        foreach (var u in users)
        {
            var offerIds = await _context.JobOffers
                .Where(j => j.CreatedByUserId == u.Id)
                .Select(j => j.Id)
                .ToListAsync();

            var received = 0;
            var realReceived = 0;
            if (offerIds.Count > 0)
            {
                received = await _context.Applications.CountAsync(a => offerIds.Contains(a.JobOfferId));

                // Candidature « reelle » : deposee par quelqu'un qui n'est pas une
                // persona du seed. Le compte peut avoir ete supprime depuis, d'ou le
                // second test sur l'adresse.
                realReceived = await _context.Applications.CountAsync(a =>
                    offerIds.Contains(a.JobOfferId)
                    && (a.UserId == null || !seedUserIds.Contains(a.UserId))
                    && !SeedAccountEmails.Contains(a.Email));
            }

            var sent = await _context.Applications.CountAsync(a => a.UserId == u.Id);

            report.Add(new SeedAccountReport(
                u.Email!, u.Role, u.CreatedAt, offerIds.Count, received, realReceived, sent));
        }

        var absent = SeedAccountEmails.Except(users.Select(u => u.Email!)).ToList();

        return Ok(new
        {
            present = users.Count,
            absent,
            // Le chiffre qui decide : a zero, ces comptes ne portent aucune donnee
            // reelle et peuvent partir. Au-dessus, il faut traiter avant.
            realApplicationsAtRisk = report.Sum(r => r.realApplicationsReceived),
            accounts = report.OrderByDescending(r => r.realApplicationsReceived).ThenBy(r => r.email),
        });
    }

    /// <summary>
    /// Supprime les offres de demonstration seedees.
    ///
    /// Les candidatures liees partent en cascade, et elles peuvent etre
    /// reelles : un visiteur a pu postuler a une offre de demonstration
    /// sans savoir qu'elle en etait une. L'appel annonce donc ce qu'il
    /// detruirait et ne detruit rien tant qu'on ne le lui redemande pas
    /// avec « confirmer=true ».
    /// </summary>
    [HttpDelete("seed-offers")]
    public async Task<ActionResult<object>> DeleteSeedOffers([FromQuery] bool confirmer = false)
    {
        var seed = await _context.JobOffers
            .Where(j => j.ExternalSource == null && SeedCompanies.Contains(j.Company))
            .ToListAsync();

        var ids = seed.Select(o => o.Id).ToList();
        var candidatures = await _context.Applications.CountAsync(a => ids.Contains(a.JobOfferId));

        if (!confirmer)
            return Conflict(new
            {
                confirmationRequise = true,
                portee = new
                {
                    offres = seed.Count,
                    candidatures,
                    entreprises = seed.Select(o => o.Company).Distinct().OrderBy(c => c),
                    titres = seed.Select(o => o.Title),
                },
                message = $"Rien n'a été supprimé. Cet appel effacerait {seed.Count} offre(s) et {candidatures} candidature(s) en cascade. Rappelez-le avec « ?confirmer=true » pour l'exécuter.",
            });

        _context.JobOffers.RemoveRange(seed);
        await _context.SaveChangesAsync();

        await _log.Log("DeleteSeedOffers", "JobOffer", null,
            $"{seed.Count} offre(s) de démonstration supprimée(s), {candidatures} candidature(s) emportée(s) en cascade",
            UserId(), UserFullName(), Ip());

        return Ok(new { deleted = seed.Count, candidatures, titles = seed.Select(o => o.Title), message = $"{seed.Count} offre(s) de démonstration supprimée(s), {candidatures} candidature(s) emportée(s)." });
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
    public async Task<ActionResult<object>> KeepOnlyFranceTravail(
        [FromQuery] bool preserverPlateforme = true, [FromQuery] bool confirmer = false)
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

        var aPartir = detail.Sum(d => d.total);
        var candidatures = await _context.Applications
            .CountAsync(a => a.JobOffer.ExternalSource != "francetravail"
                             && (!preserverPlateforme || a.JobOffer.ExternalSource != null));

        // La coupe la plus large du panneau. Elle s'annonce avant de
        // tomber, et le mode « plateforme comprise » se demande deux
        // fois : il detruit le travail des recruteurs.
        if (!confirmer)
            return Conflict(new
            {
                confirmationRequise = true,
                portee = new { offres = aPartir, candidatures, preserverPlateforme, detail },
                message = $"Rien n'a été supprimé. Cet appel effacerait {aPartir} offre(s) et {candidatures} candidature(s) en cascade"
                          + (preserverPlateforme
                              ? ". Les offres publiées sur la plateforme sont préservées."
                              : ", *y compris les offres publiées par les recruteurs*.")
                          + " Rappelez-le avec « &confirmer=true » pour l'exécuter.",
            });

        // ExecuteDelete : supprimer en base sans materialiser deux cent
        // mille entites. Les candidatures liees partent en cascade.
        var supprimees = await aSupprimer.ExecuteDeleteAsync();

        var restantes = await _context.JobOffers.CountAsync();
        await _log.Log("DeleteOffers", "JobOffer", null,
            $"{supprimees} offre(s) supprimée(s), {candidatures} candidature(s) emportée(s), {restantes} restante(s)"
            + (preserverPlateforme ? "" : " — offres de la plateforme comprises"),
            UserId(), UserFullName(), Ip());

        return Ok(new { supprimees, candidatures, restantes, detail });
    }

    /// <summary>
    /// Supprime toutes les offres d'une source d'import (ex. « adzuna »)
    /// pour les re-importer. Comme au-dessus, la portee est annoncee
    /// avant d'agir : une source mal orthographiee ne doit pas se
    /// solder par un silence, et une source juste ne doit pas emporter
    /// des candidatures sans prevenir.
    /// </summary>
    [HttpDelete("offers-by-source/{source}")]
    public async Task<ActionResult<object>> DeleteBySource(string source, [FromQuery] bool confirmer = false)
    {
        var toDelete = await _context.JobOffers.Where(j => j.ExternalSource == source).ToListAsync();

        var ids = toDelete.Select(o => o.Id).ToList();
        var candidatures = await _context.Applications.CountAsync(a => ids.Contains(a.JobOfferId));

        if (!confirmer)
            return Conflict(new
            {
                confirmationRequise = true,
                portee = new { source, offres = toDelete.Count, candidatures },
                message = toDelete.Count == 0
                    ? $"Aucune offre ne porte la source « {source} ». Rien à supprimer — vérifiez l'orthographe auprès de /admin/offers/sources."
                    : $"Rien n'a été supprimé. Cet appel effacerait {toDelete.Count} offre(s) [{source}] et {candidatures} candidature(s) en cascade. Rappelez-le avec « ?confirmer=true » pour l'exécuter.",
            });

        _context.JobOffers.RemoveRange(toDelete);
        await _context.SaveChangesAsync();

        await _log.Log("DeleteOffersBySource", "JobOffer", null,
            $"{toDelete.Count} offre(s) de source « {source} » supprimée(s), {candidatures} candidature(s) emportée(s) en cascade",
            UserId(), UserFullName(), Ip());

        return Ok(new { deleted = toDelete.Count, candidatures, message = $"{toDelete.Count} offre(s) [{source}] supprimée(s), {candidatures} candidature(s) emportée(s)." });
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

        await _log.Log("ApproveOffer", "JobOffer", id, $"Offre approuvée : {job.Title}", UserId(), UserFullName(), Ip());
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

        await _log.Log("RejectOffer", "JobOffer", id, $"Offre rejetée : {job.Title} — {dto.Note}", UserId(), UserFullName(), Ip());
        return Ok(new { job.Id, job.ModerationStatus });
    }

    [HttpPatch("moderation/{id}/feature")]
    public async Task<IActionResult> ToggleFeature(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        job.IsFeatured = !job.IsFeatured;
        await _context.SaveChangesAsync();

        await _log.Log("ToggleFeature", "JobOffer", id, $"Offre {(job.IsFeatured ? "mise en avant" : "retirée de la une")} : {job.Title}", UserId(), UserFullName(), Ip());
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

        await _log.Log("CreateAnnouncement", "Announcement", ann.Id, $"Annonce créée : {ann.Title}", UserId(), UserFullName(), Ip());
        return CreatedAtAction(nameof(GetAnnouncements), new { id = ann.Id }, ann);
    }

    [HttpDelete("announcements/{id}")]
    public async Task<IActionResult> DeleteAnnouncement(int id)
    {
        var ann = await _context.Announcements.FindAsync(id);
        if (ann == null) return NotFound();
        _context.Announcements.Remove(ann);
        await _context.SaveChangesAsync();
        await _log.Log("DeleteAnnouncement", "Announcement", id,
            $"Annonce supprimée : {ann.Title}", UserId(), UserFullName(), Ip());
        return NoContent();
    }

    [HttpPatch("announcements/{id}/toggle")]
    public async Task<IActionResult> ToggleAnnouncement(int id)
    {
        var ann = await _context.Announcements.FindAsync(id);
        if (ann == null) return NotFound();
        ann.IsActive = !ann.IsActive;
        await _context.SaveChangesAsync();
        // Une banniere qui reapparait sans qu'on sache qui l'a rallumee
        // est une question sans reponse : la bascule se journalise dans
        // les deux sens.
        await _log.Log("ToggleAnnouncement", "Announcement", id,
            $"Annonce {(ann.IsActive ? "activée" : "désactivée")} : {ann.Title}",
            UserId(), UserFullName(), Ip());
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
        [FromServices] SessionService sessions, CancellationToken ct)
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

        // L'emprunt passe par le service des sessions comme n'importe quelle
        // connexion : le jeton porte un jti, s'inscrit dans la liste des
        // appareils, et se coupe. Sans cela il echapperait a la revocation
        // — et c'est precisement le jeton qui devrait pouvoir etre coupe le
        // plus vite.
        var (jeton, finEmprunt) = await sessions.Ouvrir(
            cible, "Impersonation", HttpContext,
            TimeSpan.FromMinutes(DureeEmpruntMinutes),
            new[]
            {
                new Claim("impersonator_id", adminId),
                new Claim("impersonator_name", UserFullName()),
            });

        await _log.Log("ImpersonateStart", "User", null,
            $"Prise en main du compte {cible.Email} ({cible.Role})",
            adminId, UserFullName(), Ip());

        return Ok(new
        {
            token = jeton,
            expiration = finEmprunt,
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
        [FromServices] SessionService sessions, CancellationToken ct)
    {
        var adminId = User.FindFirstValue("impersonator_id");
        if (string.IsNullOrEmpty(adminId))
            return BadRequest(new { message = "Cette session n'est pas un emprunt." });

        var admin = await _context.Users.FirstOrDefaultAsync(u => u.Id == adminId, ct);
        if (admin == null || admin.Role != "Admin")
            return Unauthorized(new { message = "Administrateur d'origine introuvable." });

        // L'emprunt se ferme au lieu de courir jusqu'a sa demi-heure : rendre
        // la main doit vraiment la rendre.
        var jtiEmprunt = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (jtiEmprunt != null)
        {
            var session = await _context.UserSessions.FirstOrDefaultAsync(s => s.Jti == jtiEmprunt, ct);
            if (session != null)
            {
                session.RevokedAt = DateTime.UtcNow;
                session.RevokedReason = "Fin de la prise en main";
                await _context.SaveChangesAsync(ct);
            }
        }

        var (jetonAdmin, _) = await sessions.Ouvrir(admin, "Password", HttpContext, TimeSpan.FromHours(8));

        var emprunte = User.FindFirstValue(ClaimTypes.Email);
        await _log.Log("ImpersonateStop", "User", null,
            $"Fin de la prise en main du compte {emprunte}",
            admin.Id, $"{admin.FirstName} {admin.LastName}", Ip());

        return Ok(new
        {
            token = jetonAdmin,
            user = new
            {
                admin.Id, admin.Email, admin.FirstName, admin.LastName, admin.Role,
                admin.AvatarUrl, admin.Company, admin.City, admin.Title,
            },
        });
    }

    private const int DureeEmpruntMinutes = 30;

    // Les trois fabriques de jetons qui vivaient ici ont disparu : elles
    // signaient sans jti ni tampon, donc sans possibilite de revocation, et
    // la derniere se rabattait sur une cle par defaut ecrite en clair — le
    // repli meme que Program.cs refuse au demarrage. Tout passe desormais
    // par SessionService.

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

    /// <summary>
    /// Nombre de jours au-dela duquel une candidature sans decision est
    /// consideree en souffrance. Partage par la facette et par le filtre :
    /// une tuile qui compte autrement que ce que son clic affiche ment.
    /// </summary>
    private const int StaleAfterDays = 30;

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
        [FromQuery] string? source,
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
        // « local » ne se compare pas a ExternalSource : c'est son absence.
        if (source == "local") query = query.Where(j => j.ExternalSource == null);
        else if (!string.IsNullOrWhiteSpace(source)) query = query.Where(j => j.ExternalSource == source);
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
            Local = g.Count(j => j.ExternalSource == null),
            NoSalary = g.Count(j => j.MinSalary == null && j.MaxSalary == null),
        }).FirstOrDefaultAsync() ?? new OfferFacetsDto();

        query = sort switch
        {
            "views" => query.OrderByDescending(j => j.ViewCount),
            "title" => query.OrderBy(j => j.Title),
            "company" => query.OrderBy(j => j.Company).ThenBy(j => j.Title),
            // Trier par salaire remonte les extremes : c'est ainsi qu'on
            // reconnait une remuneration mal analysee a l'import.
            "salary" => query.OrderByDescending(j => j.MinSalary ?? j.MaxSalary),
            "salary_asc" => query.Where(j => j.MinSalary != null || j.MaxSalary != null)
                                 .OrderBy(j => j.MinSalary ?? j.MaxSalary),
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
                j.MinSalary, j.MaxSalary, j.SalaryPeriod, j.ExternalSource,
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
        [FromQuery] bool? stale,
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

        // Le delai de courtoisie au-dela duquel un dossier sans decision
        // devient un candidat abandonne. Trente jours est la limite
        // habituellement retenue par les chartes de recrutement.
        var limiteRelance = DateTime.UtcNow.AddDays(-StaleAfterDays);
        if (stale == true)
            query = query.Where(a => (a.Status == "Pending" || a.Status == "Reviewed") && a.AppliedAt < limiteRelance);

        var facets = await query.GroupBy(_ => 1).Select(g => new ApplicationFacetsDto
        {
            Total = g.Count(),
            Pending = g.Count(a => a.Status == "Pending"),
            Reviewed = g.Count(a => a.Status == "Reviewed"),
            Accepted = g.Count(a => a.Status == "Accepted"),
            Rejected = g.Count(a => a.Status == "Rejected"),
            Stale = g.Count(a => (a.Status == "Pending" || a.Status == "Reviewed") && a.AppliedAt < limiteRelance),
            // Moyenne sur les seuls dossiers lus : inclure les autres avec
            // un delai nul ferait baisser la mesure a mesure que le retard
            // s'accumule, soit exactement l'inverse de ce qu'elle dit.
            AvgResponseDays = g.Where(a => a.ReviewedAt != null)
                               .Average(a => (double?)EF.Functions.DateDiffDay(a.AppliedAt, a.ReviewedAt!.Value)),
        }).FirstOrDefaultAsync() ?? new ApplicationFacetsDto();

        query = sort switch
        {
            "candidate" => query.OrderBy(a => a.FullName),
            "company" => query.OrderBy(a => a.JobOffer!.Company),
            "status" => query.OrderBy(a => a.Status),
            // Les plus anciennes d'abord : l'ordre dans lequel il faudrait
            // les traiter, et celui que la liste ne donnait pas.
            "oldest" => query.OrderBy(a => a.AppliedAt),
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
        [FromQuery] bool? upcoming,
        [FromQuery] bool? overdue,
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

        // Un entretien dont la date est passee mais qui reste « propose »
        // ou « accepte » n'a jamais ete cloture : personne n'a dit s'il a
        // eu lieu. C'est le seul defaut de cette page sur lequel
        // l'exploitant peut agir, et rien ne le signalait.
        if (upcoming == true) query = query.Where(i => i.ProposedAt > now && i.Status != "Cancelled");
        if (overdue == true)
            query = query.Where(i => i.ProposedAt < now && (i.Status == "Proposed" || i.Status == "Accepted"));

        var facets = await query.GroupBy(_ => 1).Select(g => new InterviewFacetsDto
        {
            Total = g.Count(),
            Proposed = g.Count(i => i.Status == "Proposed"),
            Accepted = g.Count(i => i.Status == "Accepted"),
            Completed = g.Count(i => i.Status == "Completed"),
            Cancelled = g.Count(i => i.Status == "Cancelled"),
            Upcoming = g.Count(i => i.ProposedAt > now && i.Status != "Cancelled"),
            Overdue = g.Count(i => i.ProposedAt < now && (i.Status == "Proposed" || i.Status == "Accepted")),
        }).FirstOrDefaultAsync() ?? new InterviewFacetsDto();

        query = sort switch
        {
            "candidate" => query.OrderBy(i => i.Application.FullName),
            "company" => query.OrderBy(i => i.Application.JobOffer!.Company),
            "status" => query.OrderBy(i => i.Status),
            // Le plus recent d'abord : l'ordre de lecture d'un historique,
            // quand la question porte sur ce qui vient de se passer.
            "recent" => query.OrderByDescending(i => i.ProposedAt),
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
            // Un administrateur sans second facteur est le maillon faible de
            // toute la plateforme : cela se voit depuis la liste, sans avoir
            // a ouvrir les fiches une par une.
            u.TwoFactorEnabled,
            u.EmailConfirmed,
            verrouille = u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow,
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
            $"Candidature de {a.FullName} modifiée", UserId(), UserFullName(), Ip());
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
            $"Candidature de {a.FullName} supprimée", UserId(), UserFullName(), Ip());
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
        await _log.Log("UpdateSavedSearch", "SavedSearch", id,
            $"Recherche « {s.Label} » de {await Concerne(s.UserId)} modifiée — alerte {(s.AlertEnabled ? "active" : "coupée")}",
            UserId(), UserFullName(), Ip());
        return Ok(new { s.Id, s.AlertEnabled, s.Label });
    }

    [HttpDelete("saved-searches/{id}")]
    public async Task<IActionResult> SupprimerRecherche(int id)
    {
        var s = await _context.SavedSearches.FindAsync(id);
        if (s == null) return NotFound();
        var qui = await Concerne(s.UserId);
        _context.SavedSearches.Remove(s);
        await _context.SaveChangesAsync();
        await _log.Log("DeleteSavedSearch", "SavedSearch", id,
            $"Recherche « {s.Label} » de {qui} supprimée", UserId(), UserFullName(), Ip());
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
            "Entretien modifié", UserId(), UserFullName(), Ip());
        return Ok(new { i.Id, i.Status });
    }

    [HttpDelete("interviews/{id}")]
    public async Task<IActionResult> SupprimerEntretien(int id)
    {
        // Charge avec la candidature : un entretien annule concerne deux
        // personnes qui l'avaient inscrit a leur agenda, et la trace doit
        // dire lesquelles.
        var i = await _context.Interviews
            .Include(x => x.Application).ThenInclude(a => a.JobOffer)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (i == null) return NotFound();
        var quoi = $"Entretien du {i.ProposedAt:dd/MM/yyyy à HH:mm} avec {i.Application.FullName} "
                 + $"pour « {i.Application.JobOffer.Title} » supprimé";
        _context.Interviews.Remove(i);
        await _context.SaveChangesAsync();
        await _log.Log("DeleteInterview", "Interview", id, quoi, UserId(), UserFullName(), Ip());
        return NoContent();
    }

    [HttpDelete("job-notes/{id}")]
    public async Task<IActionResult> SupprimerNote(int id)
    {
        var n = await _context.JobNotes.FindAsync(id);
        if (n == null) return NotFound();
        var qui = await Concerne(n.UserId);
        _context.JobNotes.Remove(n);
        await _context.SaveChangesAsync();
        await _log.Log("DeleteJobNote", "JobNote", id,
            $"Note de {qui} sur l'offre {n.JobOfferId} supprimée", UserId(), UserFullName(), Ip());
        return NoContent();
    }

    [HttpPatch("cv-sections/{id}")]
    public async Task<IActionResult> ModifierSectionCv(int id, [FromBody] SectionCvDto dto)
    {
        var c = await _context.CvSections.FindAsync(id);
        if (c == null) return NotFound();
        var avant = c.Title;
        if (dto.Titre != null) c.Title = dto.Titre;
        if (dto.Organisation != null) c.Organization = dto.Organisation;
        if (dto.Lieu != null) c.Location = dto.Lieu;
        if (dto.Description != null) c.Description = dto.Description;
        if (dto.Niveau != null) c.Level = dto.Niveau;
        await _context.SaveChangesAsync();
        // Ecrire dans le CV de quelqu'un d'autre est precisement ce qu'un
        // journal existe pour rendre visible.
        await _log.Log("UpdateCvSection", "CvSection", id,
            $"Section « {avant} » du CV de {await Concerne(c.UserId)} modifiée"
            + (dto.Titre != null && dto.Titre != avant ? $" — devenue « {c.Title} »" : ""),
            UserId(), UserFullName(), Ip());
        return Ok(new { c.Id, c.Title });
    }

    [HttpDelete("cv-sections/{id}")]
    public async Task<IActionResult> SupprimerSectionCv(int id)
    {
        var c = await _context.CvSections.FindAsync(id);
        if (c == null) return NotFound();
        var qui = await Concerne(c.UserId);
        _context.CvSections.Remove(c);
        await _context.SaveChangesAsync();
        await _log.Log("DeleteCvSection", "CvSection", id,
            $"Section « {c.Title} » du CV de {qui} supprimée", UserId(), UserFullName(), Ip());
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

        // Le dossier ne connaissait qu'un metier : celui de candidat. Un
        // recruteur ouvrait donc une fiche de zeros — aucune candidature,
        // aucune alerte, aucun CV, aucune note — alors qu'il publie des
        // offres, recoit des candidatures et mene des entretiens. La base
        // le sait par CreatedByUserId ; la fiche ne le lui demandait pas.
        var offresPubliees = await _context.JobOffers
            .Where(o => o.CreatedByUserId == id)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id, o.Title, o.Company, o.Location, o.ContractType, o.Category,
                o.CreatedAt, o.ExpiresAt, o.IsActive, o.IsDraft, o.ModerationStatus,
                o.ViewCount, o.ExternalSource, o.Openings,
                applications = o.Applications.Count,
                pending = o.Applications.Count(a => a.Status == "Pending"),
                hired = o.Applications.Count(a => a.Status == "Accepted"),
            })
            .ToListAsync();

        // Les candidatures recues sont bornees : une fiche n'est pas le
        // tableau des candidatures, elle en donne la mesure et y renvoie.
        var candidaturesRecues = await _context.Applications
            .Where(a => a.JobOffer.CreatedByUserId == id)
            .OrderByDescending(a => a.AppliedAt)
            .Take(60)
            .Select(a => new
            {
                a.Id, a.JobOfferId, a.Status, a.AppliedAt, a.ReviewedAt, a.IsArchived,
                a.FullName, a.City, a.QualificationScore, a.UserId,
                jobTitle = a.JobOffer.Title, company = a.JobOffer.Company,
            })
            .ToListAsync();

        var entretiensMenes = await _context.Interviews
            .Where(i => i.Application.JobOffer.CreatedByUserId == id)
            .OrderByDescending(i => i.ProposedAt)
            .Select(i => new
            {
                i.Id, i.ApplicationId, i.ProposedAt, i.Status, i.Type, i.Duration,
                i.Location, i.InterviewerName,
                candidat = i.Application.FullName,
                jobTitle = i.Application.JobOffer.Title,
            })
            .ToListAsync();

        // Le delai de reponse d'un recruteur se mesure sur les seules
        // candidatures qu'il a effectivement lues : compter les autres
        // comme instantanees le flatterait, les compter comme infinies le
        // condamnerait.
        var lues = await _context.Applications
            .Where(a => a.JobOffer.CreatedByUserId == id && a.ReviewedAt != null)
            .Select(a => EF.Functions.DateDiffHour(a.AppliedAt, a.ReviewedAt!.Value))
            .ToListAsync();
        double? delaiReponseJours = lues.Count > 0 ? Math.Round(lues.Average() / 24.0, 1) : null;

        // « Ce compte sert-il encore ? » est la premiere question que pose
        // une fiche, et rien n'y repondait : AppUser ne porte pas de date
        // de derniere connexion. Le journal la porte deja — la deduire
        // evite d'ajouter une colonne et une migration pour une donnee qui
        // existe.
        var connexions = _context.ActivityLogs.Where(l => l.UserId == id && l.Action == "Login");
        var derniereConnexion = await connexions.MaxAsync(l => (DateTime?)l.CreatedAt);
        var depuis30j = DateTime.UtcNow.AddDays(-30);
        var connexions30j = await connexions.CountAsync(l => l.CreatedAt >= depuis30j);

        return new
        {
            user = new
            {
                user.Id, user.Email, user.FirstName, user.LastName, user.Role,
                user.AvatarUrl, user.Company, user.Bio, user.ResumeUrl, user.Title,
                user.Skills, user.ExperienceYears, user.Education, user.City,
                user.LinkedInUrl, user.PortfolioUrl, user.IsSearchable, user.IsActive,
                user.CreatedAt, user.PhoneNumber, user.EmailConfirmed,
                lastLoginAt = user.LastLoginAt ?? derniereConnexion,
                loginsLast30Days = connexions30j,
            },

            // Ce que vaut la protection de ce compte. Une fiche qui parle de
            // suspendre et de prendre la main sans dire si le compte est
            // protege par un second facteur passe a cote de la question.
            securite = new
            {
                deuxFacteurs = user.TwoFactorEnabled,
                methode = user.TwoFactorEnabled ? (user.TwoFactorMethod ?? "Totp") : null,
                telephone = OvhSmsService.Masquer(user.PhoneNumber),
                deuxFacteursDepuis = user.TwoFactorEnabledAt,
                deuxFacteursObligatoire = user.Role == "Admin",
                emailConfirme = user.EmailConfirmed,
                motDePasseModifieLe = user.LastPasswordChangedAt,
                verrouilleJusquA = user.LockoutEnd,
                echecs = user.AccessFailedCount,
                sessions = await _context.UserSessions
                    .Where(s => s.UserId == id && s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow)
                    .OrderByDescending(s => s.LastSeenAt)
                    .Take(20)
                    .Select(s => new { s.Id, s.Device, s.IpAddress, s.CreatedAt, s.LastSeenAt, s.Method })
                    .ToListAsync(),
            },
            applications,
            savedSearches,
            interviews,
            cvSections,
            notes,
            activity,
            offresPubliees,
            candidaturesRecues,
            entretiensMenes,
            delaiReponseJours,
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

        // La soupape de la confirmation d'adresse.
        //
        // Publier une offre et ecrire a un candidat exigent une adresse
        // confirmee. Si le message ne parvient jamais — filtre trop
        // zele, quota d'expedition atteint, adresse professionnelle qui
        // rejette l'inconnu — le recruteur est bloque sans recours. On
        // doit pouvoir le debloquer apres verification, et cela se
        // journalise : confirmer a la place de quelqu'un est un acte.
        if (dto.EmailConfirmed.HasValue && dto.EmailConfirmed.Value != user.EmailConfirmed)
        {
            user.EmailConfirmed = dto.EmailConfirmed.Value;
            await _log.Log("ConfirmEmailManuel", "User", null,
                $"Adresse de {user.Email} marquée {(user.EmailConfirmed ? "confirmée" : "non confirmée")} par l'administration",
                UserId(), UserFullName(), Ip());
        }

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
            $"Profil de {user.Email} modifié", UserId(), UserFullName(), Ip());

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

        // Un mot de passe qu'on reinitialise l'est presque toujours parce
        // qu'on soupconne l'ancien : laisser les sessions ouvertes rendrait
        // le geste vain. Identity a deja renouvele le tampon de securite, ce
        // qui coupe les jetons ; on ferme les sessions pour que la liste
        // d'appareils le dise aussi.
        var sessions = HttpContext.RequestServices.GetRequiredService<SessionService>();
        await sessions.RevoquerToutes(user.Id, "Mot de passe reinitialise par l'administration");

        await _log.Log("ResetPassword", "User", null,
            $"Mot de passe de {user.Email} réinitialisé", UserId(), UserFullName(), Ip());

        return Ok(new { message = "Mot de passe réinitialisé. Tous les appareils ont été déconnectés." });
    }

    // ═══════════════════════════════════
    //  4 quater. SECURITE D'UN COMPTE
    //
    //  L'administration n'a ici que les pouvoirs qu'elle ne peut pas ne pas
    //  avoir : deverrouiller quelqu'un que le compteur d'echecs a enferme
    //  dehors, et couper la double authentification de qui a perdu a la fois
    //  son telephone et ses codes de secours. Les deux laissent une trace
    //  nominative — ce sont precisement les gestes dont on veut pouvoir dire
    //  plus tard qui les a faits.
    // ═══════════════════════════════════

    /// <summary>Deverrouille un compte bloque par le compteur de tentatives.</summary>
    [HttpPost("users/{id}/deverrouiller")]
    public async Task<IActionResult> Deverrouiller(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        await _log.Log("Deverrouillage", "User", null,
            $"Compte {user.Email} deverrouille", UserId(), UserFullName(), Ip());

        return Ok(new { message = "Compte déverrouillé." });
    }

    /// <summary>
    /// Coupe la double authentification d'un compte. Recours de derniere
    /// extremite : la personne a perdu son telephone et ses codes de secours,
    /// et sans cela son compte serait definitivement clos.
    /// </summary>
    [HttpPost("users/{id}/2fa/desactiver")]
    public async Task<IActionResult> DesactiverDeuxFacteurs(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
            return BadRequest(new { message = "La double authentification n'est pas active sur ce compte." });

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        user.TwoFactorEnabledAt = null;
        user.TwoFactorMethod = null;
        await _userManager.UpdateAsync(user);

        var mail = HttpContext.RequestServices.GetRequiredService<IEmailSender>();
        var site = (HttpContext.RequestServices.GetRequiredService<IConfiguration>()["App:PublicUrl"] ?? "").TrimEnd('/');
        await mail.Envoyer(ModelesCourriel.DoubleAuthentification(user.Email!, user.FirstName, false, $"{site}/securite"));

        await _log.Log("2faDesactiveeParAdmin", "User", null,
            $"Double authentification de {user.Email} desactivee par l'administration",
            UserId(), UserFullName(), Ip());

        return Ok(new { message = "Double authentification désactivée. La personne en a été informée par courriel." });
    }

    /// <summary>Ferme tous les appareils connectes d'un compte.</summary>
    [HttpPost("users/{id}/sessions/tout-fermer")]
    public async Task<ActionResult<object>> FermerSessions(string id, [FromServices] SessionService sessions)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var n = await sessions.RevoquerToutes(id, "Deconnexion demandee par l'administration");
        await _log.Log("SessionsFermees", "User", null,
            $"{n} session(s) de {user.Email} fermee(s)", UserId(), UserFullName(), Ip());

        return Ok(new { fermees = n, message = n == 0 ? "Aucun appareil n'était connecté." : $"{n} appareil(s) deconnecte(s)." });
    }

    // ═══════════════════════════════════
    //  4 quinquies. EXPEDITION DE COURRIEL
    // ═══════════════════════════════════

    /// <summary>
    /// Ce que la plateforme sait de son serveur d'expedition. Aucun secret
    /// n'en sort : l'hote et l'expediteur suffisent a diagnostiquer, le mot
    /// de passe n'aiderait personne.
    /// </summary>
    [HttpGet("email/etat")]
    public ActionResult<object> EtatCourriel([FromServices] IEmailSender mail)
        => Ok(new
        {
            configure = mail.EstConfigure,
            etat = mail.Etat,
            consequence = mail.EstConfigure
                ? "Les mots de passe oubliés, les confirmations d'adresse et les alertes de connexion partent normalement."
                : "Aucun message ne part. « Mot de passe oublié » n'aboutit pas, les adresses ne se confirment pas, et les alertes de connexion sont écrites dans le journal du serveur au lieu d'être envoyées.",
        });

    /// <summary>
    /// Ce que la plateforme sait de son compte SMS. Aucun secret n'en sort :
    /// le point d'entree et le nom du compte suffisent a diagnostiquer.
    /// </summary>
    [HttpGet("sms/etat")]
    public ActionResult<object> EtatSms([FromServices] OvhSmsService sms)
        => Ok(new
        {
            configure = sms.EstConfigure,
            etat = sms.Etat,
            consequence = sms.EstConfigure
                ? "Le second facteur par SMS est proposé aux membres, et les codes partent."
                : "Le second facteur par SMS n'est pas proposé : seule l'application d'authentification l'est. Les identifiants OVH manquent.",
        });

    /// <summary>Envoie un message de controle a l'adresse demandee.</summary>
    [HttpPost("email/essai")]
    public async Task<ActionResult<object>> EssaiCourriel([FromServices] IEmailSender mail, [FromBody] EssaiCourrielDto dto)
    {
        var destinataire = string.IsNullOrWhiteSpace(dto?.Email) ? User.FindFirstValue(ClaimTypes.Email) : dto!.Email;
        if (string.IsNullOrWhiteSpace(destinataire))
            return BadRequest(new { message = "Aucune adresse de destination." });

        var parti = await mail.Envoyer(ModelesCourriel.Essai(destinataire));

        // Un essai part vers une adresse choisie librement. C'est peu de
        // chose, et c'est justement pourquoi il doit laisser une trace :
        // le seul moyen de savoir que le serveur d'envoi a servi a autre
        // chose qu'a un controle.
        await _log.Log("TestEmail", "Email", null,
            $"Message de contrôle {(parti ? "expédié" : "refusé")} vers {destinataire}",
            UserId(), UserFullName(), Ip());

        return Ok(new
        {
            parti,
            message = parti
                ? $"Message expedie a {destinataire}. S'il n'arrive pas, regardez les indésirables avant de soupçonner la configuration."
                : "Rien n'est parti : aucun serveur n'est configuré, ou il a refusé la connexion. Le détail se trouve dans le journal du serveur.",
        });
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

        await _log.Log("UpdateSettings", "PlatformSetting", null, $"Paramètres mis à jour ({settings.Count} modification(s))", UserId(), UserFullName(), Ip());
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
        // Les mentions legales alimentent des pages publiques : elles se lisent
        // sans authentification, comme le reste de ce lot.
        var publicKeys = new[]
        {
            "welcome_message", "contact_email", "allow_registration", "require_moderation",
            "maintenance_mode", "max_applications_per_candidate",
            "newsletter_auto_redaction",
            "legal_raison_sociale", "legal_adresse", "legal_siret", "legal_tva",
            "legal_telephone", "legal_directeur_publication", "legal_hebergeur", "legal_dpo",
            "legal_conservation_compte", "legal_conservation_candidatures", "legal_conservation_journal",
        };
        var settings = await _context.PlatformSettings
            .Where(s => publicKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        return Ok(settings);
    }
}

// ── DTOs ──
public class ModerationNoteDto
{
    [Longueur(Limites.Paragraphe)]
    public string? Note { get; set; }
}

/// <summary>
/// Edition d'un compte par l'administration. Tout est optionnel : null
/// signifie « ne touche pas a ce champ », pas « efface-le ».
/// </summary>
public class AdminUserUpdateDto
{
    [Longueur(100)] public string? FirstName { get; set; }
    [Longueur(100)] public string? LastName { get; set; }
    [AdresseCourriel] public string? Email { get; set; }

    /// <summary>
    /// Debloque un compte dont le message de confirmation n'est jamais
    /// arrive. A n'utiliser qu'apres avoir verifie autrement que
    /// l'adresse est bien la sienne.
    /// </summary>
    public bool? EmailConfirmed { get; set; }
    [TelephoneFr] public string? PhoneNumber { get; set; }
    [Longueur(150)] public string? Title { get; set; }
    [Longueur(500)] public string? Bio { get; set; }
    [Longueur(500)] public string? Skills { get; set; }
    [Longueur(200)] public string? Education { get; set; }
    [Longueur(100)] public string? City { get; set; }
    [Longueur(200)] public string? Company { get; set; }
    [AdresseWeb(ExterneSeulement = true)] public string? LinkedInUrl { get; set; }
    [AdresseWeb(ExterneSeulement = true)] public string? PortfolioUrl { get; set; }
    [AdresseWeb] public string? AvatarUrl { get; set; }
    [AdresseWeb] public string? ResumeUrl { get; set; }
    [Range(0, 70, ErrorMessage = "Le nombre d'années d'expérience doit être compris entre 0 et 70.")]
    public int? ExperienceYears { get; set; }
    public bool? IsSearchable { get; set; }
    public bool? IsActive { get; set; }
}

public class AdminPasswordDto
{
    // Huit et non six : la meme exigence que partout ailleurs. Un mot de
    // passe pose par l'administration n'a pas de raison d'etre plus faible
    // que celui que l'interesse choisirait lui-meme.
    [Required(ErrorMessage = "Choisissez un mot de passe.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Le mot de passe fait entre 8 et 128 caractères.")]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// Modification d'une piece du dossier. Tout est optionnel : null
/// signifie « ne touche pas », pas « efface ».
/// </summary>
public class PieceDossierDto
{
    [Longueur(30)] public string? Statut { get; set; }
    public bool? Archivee { get; set; }
    public bool? AlerteActive { get; set; }
    [Longueur(200)] public string? Libelle { get; set; }
    [Longueur(2000)] public string? Notes { get; set; }
}

public class SectionCvDto
{
    [Longueur(200)] public string? Titre { get; set; }
    [Longueur(200)] public string? Organisation { get; set; }
    [Longueur(200)] public string? Lieu { get; set; }
    [Longueur(2000)] public string? Description { get; set; }
    [Longueur(100)] public string? Niveau { get; set; }
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

    /// <summary>
    /// Offres deposees sur la plateforme, par opposition aux offres importees
    /// chez un partenaire. Le catalogue est massivement importe : c'est le
    /// nombre qui dit ce que la plateforme produit vraiment.
    /// </summary>
    public int Local { get; set; }

    /// <summary>Offres dont la remuneration reste inconnue apres analyse.</summary>
    public int NoSalary { get; set; }
}

public class ApplicationFacetsDto
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Reviewed { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }

    /// <summary>
    /// Dossiers sans decision passe le delai de courtoisie. Un candidat
    /// laisse sans reponse ne revient pas : c'est la seule mesure de cette
    /// page sur laquelle l'exploitant peut agir.
    /// </summary>
    public int Stale { get; set; }

    /// <summary>Delai moyen, en jours, entre le depot et la premiere lecture.</summary>
    public double? AvgResponseDays { get; set; }
}

public class InterviewFacetsDto
{
    /// <summary>Entretiens passes sans issue enregistree.</summary>
    public int Overdue { get; set; }

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
    [Required(ErrorMessage = "Donnez un titre à l'annonce.")]
    [StringLength(Limites.Ligne, MinimumLength = 2)]
    [SansBalisage]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Écrivez le message de l'annonce.")]
    [StringLength(Limites.Paragraphe, MinimumLength = 2)]
    public string Message { get; set; } = string.Empty;

    [Longueur(Limites.Nom), SansBalisage]
    public string? Type { get; set; }

    [Parmi("Candidate", "Recruiter", "Admin", "All")]
    public string? TargetRole { get; set; }

    public bool IsBanner { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
}

public class SettingUpdateDto
{
    [Required, Longueur(Limites.Nom), SansBalisage]
    public string Key { get; set; } = string.Empty;

    [Longueur(Limites.Paragraphe)]
    public string Value { get; set; } = string.Empty;

    [Longueur(Limites.Nom), SansBalisage]
    public string? Type { get; set; }

    [Longueur(Limites.Ligne)]
    public string? Description { get; set; }
}

public class EssaiCourrielDto
{
    /// <summary>Vide, le message part a l'adresse de l'administrateur connecte.</summary>
    [AdresseCourriel]
    public string? Email { get; set; }
}
