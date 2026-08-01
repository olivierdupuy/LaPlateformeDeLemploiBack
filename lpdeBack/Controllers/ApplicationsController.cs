using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.DTOs;
using lpdeBack.Hubs;
using lpdeBack.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApplicationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly PerimetreRecruteur _perimetre;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly PushNotificationService _pushService;
    private readonly ActivityLogService _log;
    private readonly IEmailSender _mail;
    private readonly IConfiguration _config;
    private readonly ILogger<ApplicationsController> _journal;

    public ApplicationsController(AppDbContext context, IHubContext<ChatHub> hubContext,
                                  PushNotificationService pushService, ActivityLogService log,
                                  IEmailSender mail, IConfiguration config,
                                  ILogger<ApplicationsController> journal, PerimetreRecruteur perimetre)
    {
        _perimetre = perimetre;
        _log = log;
        _context = context;
        _hubContext = hubContext;
        _pushService = pushService;
        _mail = mail;
        _config = config;
        _journal = journal;
    }

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin() => User.IsInRole("Admin");
    private string SiteUrl => (_config["App:PublicUrl"] ?? "").TrimEnd('/');

    /// <summary>
    /// Expedie un courriel sans jamais faire echouer ce qui l'a declenche.
    ///
    /// Une candidature enregistree est un fait acquis : si Brevo est en
    /// panne, si l'adresse rebondit, si le reseau tousse, cela ne doit pas
    /// rendre une erreur a quelqu'un qui vient de postuler — il croirait
    /// devoir recommencer, et le doublon serait refuse.
    ///
    /// L'echec se journalise, et rien de plus. C'est le seul endroit du
    /// code ou avaler une exception est la bonne conduite.
    /// </summary>
    private async Task Prevenir(Courriel? message, string quoi)
    {
        if (message == null) return;
        try
        {
            await _mail.Envoyer(message);
        }
        catch (Exception ex)
        {
            _journal.LogWarning(ex, "Courriel « {Quoi} » non expedie a {Destinataire}",
                                quoi, message.Destinataire);
        }
    }

    /// <summary>Vrai si une question de preselection obligatoire reste sans
    /// reponse. Le tunnel de candidature l'empeche deja, mais l'API reste
    /// atteignable directement.</summary>
    private static bool HasMissingRequiredAnswers(string? questionsJson, string? answersJson)
    {
        if (string.IsNullOrWhiteSpace(questionsJson)) return false;

        try
        {
            using var questions = JsonDocument.Parse(questionsJson);
            if (questions.RootElement.ValueKind != JsonValueKind.Array) return false;
            if (questions.RootElement.GetArrayLength() == 0) return false;

            var given = new List<string>();
            if (!string.IsNullOrWhiteSpace(answersJson))
            {
                using var answers = JsonDocument.Parse(answersJson);
                if (answers.RootElement.ValueKind == JsonValueKind.Array)
                {
                    given = answers.RootElement.EnumerateArray()
                        .Select(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("answer", out var v)
                            ? v.GetString() ?? string.Empty
                            : string.Empty)
                        .ToList();
                }
            }

            var index = 0;
            foreach (var q in questions.RootElement.EnumerateArray())
            {
                // Ancien format (chaine nue) : la reponse a toujours ete exigee.
                var required = q.ValueKind != JsonValueKind.Object
                    || !q.TryGetProperty("required", out var r)
                    || r.ValueKind != JsonValueKind.False;

                if (required && (index >= given.Count || string.IsNullOrWhiteSpace(given[index])))
                    return true;
                index++;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Part des criteres de preselection satisfaits, en pourcentage.
    /// Le recruteur peut associer une reponse ideale a ses questions ; on compare
    /// les reponses du candidat, dans l'ordre des questions. Renvoie null si
    /// l'offre ne definit aucune reponse ideale (rien a mesurer).</summary>
    private static int? ComputeQualificationScore(string? questionsJson, string? answersJson)
    {
        if (string.IsNullOrWhiteSpace(questionsJson) || string.IsNullOrWhiteSpace(answersJson))
            return null;

        try
        {
            using var questions = JsonDocument.Parse(questionsJson);
            using var answers = JsonDocument.Parse(answersJson);
            if (questions.RootElement.ValueKind != JsonValueKind.Array
                || answers.RootElement.ValueKind != JsonValueKind.Array) return null;

            var given = answers.RootElement.EnumerateArray()
                .Select(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("answer", out var v)
                    ? v.GetString() ?? string.Empty
                    : string.Empty)
                .ToList();

            int expected = 0, met = 0;
            var index = 0;
            foreach (var q in questions.RootElement.EnumerateArray())
            {
                // L'ancien format (simple chaine) ne porte aucune reponse ideale.
                if (q.ValueKind == JsonValueKind.Object
                    && q.TryGetProperty("idealAnswer", out var ideal)
                    && ideal.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(ideal.GetString()))
                {
                    expected++;
                    if (index < given.Count
                        && string.Equals(given[index].Trim(), ideal.GetString()!.Trim(), StringComparison.OrdinalIgnoreCase))
                        met++;
                }
                index++;
            }

            return expected == 0 ? null : (int)Math.Round(met * 100.0 / expected);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Recruteur/Admin ──

    [HttpGet]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<IEnumerable<Application>>> GetAll()
    {
        var userId = GetUserId();
        var query = _context.Applications.Include(a => a.JobOffer).AsQueryable();
        if (!IsAdmin())
        {
            var equipe = await _perimetre.Equipe(userId);
            query = query.Where(a => a.JobOffer.CreatedByUserId != null
                                     && equipe.Contains(a.JobOffer.CreatedByUserId));
        }
        return await query.OrderByDescending(a => a.AppliedAt).ToListAsync();
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<Application>> GetById(int id)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), app.JobOffer.CreatedByUserId)) return Forbid();
        return app;
    }

    [HttpGet("job/{jobOfferId}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<IEnumerable<Application>>> GetByJobOffer(int jobOfferId)
    {
        var job = await _context.JobOffers.FindAsync(jobOfferId);
        if (job == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), job.CreatedByUserId)) return Forbid();
        return await _context.Applications.Where(a => a.JobOfferId == jobOfferId).OrderByDescending(a => a.AppliedAt).ToListAsync();
    }

    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> UpdateStatus(int id, ApplicationUpdateStatusDto dto)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), app.JobOffer.CreatedByUserId)) return Forbid();

        var validStatuses = new[] { "Pending", "Reviewed", "Accepted", "Rejected" };
        if (!validStatuses.Contains(dto.Status)) return BadRequest("Statut invalide.");

        var statusLabels = new Dictionary<string, string>
        {
            {"Pending", "en attente"}, {"Reviewed", "examinee"}, {"Accepted", "acceptee"}, {"Rejected", "refusee"}
        };

        app.Status = dto.Status;
        // Horodate la premiere consultation, pour l'afficher au candidat.
        if (dto.Status == "Reviewed" && app.ReviewedAt == null)
            app.ReviewedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Notification au candidat
        if (app.UserId != null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = app.UserId,
                Title = "Statut de candidature modifie",
                Message = $"Votre candidature pour \"{app.JobOffer.Title}\" chez {app.JobOffer.Company} est maintenant {statusLabels.GetValueOrDefault(dto.Status, dto.Status)}.",
                Link = "/suivi",
                Type = "StatutModifie"
            });
            await _context.SaveChangesAsync();

            // Real-time notification to candidate
            foreach (var connId in ChatHub.GetConnectionIds(app.UserId))
            {
                await _hubContext.Clients.Client(connId).SendAsync("ApplicationStatusChanged", new
                {
                    applicationId = app.Id,
                    status = dto.Status,
                    jobTitle = app.JobOffer.Title,
                    company = app.JobOffer.Company
                });
                await _hubContext.Clients.Client(connId).SendAsync("NewNotification");
            }

            // Push notification (mobile will suppress if app is in foreground)
            await _pushService.SendToUser(app.UserId, "Statut de candidature modifie",
                $"Votre candidature pour \"{app.JobOffer.Title}\" est maintenant {statusLabels.GetValueOrDefault(dto.Status, dto.Status)}.",
                "/tabs/applications");

            // C'est la reponse qu'on attend le plus, et c'est celle qui ne
            // partait nulle part. Un refus annonce vaut mieux qu'un silence,
            // et personne ne revient chaque jour verifier sur un site.
            var candidat = await _context.Users.FirstOrDefaultAsync(u => u.Id == app.UserId);
            if (candidat?.Email != null)
                await Prevenir(ModelesCourriel.StatutCandidature(
                    candidat.Email, candidat.FirstName, app.JobOffer.Title,
                    app.JobOffer.Company, dto.Status, $"{SiteUrl}/suivi"),
                    "statut de candidature");
        }

        return NoContent();
    }

    [HttpPatch("{id}/notes")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> UpdateNotes(int id, ApplicationUpdateNotesDto dto)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), app.JobOffer.CreatedByUserId)) return Forbid();

        app.RecruiterNotes = dto.Notes;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Delete(int id)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), app.JobOffer.CreatedByUserId)) return Forbid();

        // La suppression par le recruteur n'ecrivait rien au journal, alors
        // que celle de l'administration le fait. Une candidature pouvait
        // donc disparaitre definitivement sans qu'aucune trace n'en
        // subsiste : ni qui l'a supprimee, ni quand, ni pour quelle offre.
        // Sur une plateforme de recrutement, l'effacement du dossier de
        // quelqu'un est precisement ce qui doit se raconter apres coup.
        var nom = app.FullName;
        var offre = app.JobOffer?.Title ?? $"offre #{app.JobOfferId}";

        _context.Applications.Remove(app);
        await _context.SaveChangesAsync();

        await _log.Log("DeleteApplication", "Application", id,
            $"Candidature de {nom} supprimée — {offre}",
            GetUserId(), $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}".Trim(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return NoContent();
    }

    /// <summary>Stats pour le recruteur connecte (ses offres uniquement)</summary>
    [HttpGet("stats/recruiter")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<object>> RecruiterStats()
    {
        var userId = GetUserId();
        var equipe = await _perimetre.Equipe(userId);
        var myOffers = _context.JobOffers.Where(j => j.CreatedByUserId != null
                                                     && equipe.Contains(j.CreatedByUserId));
        var myOfferIds = myOffers.Select(j => j.Id);
        var myApps = _context.Applications.Where(a => myOfferIds.Contains(a.JobOfferId));

        var totalOffres = await myOffers.CountAsync();
        var offresActives = await myOffers.CountAsync(j => j.IsActive);
        var offresExpirees = await myOffers.CountAsync(j => !j.IsActive);
        var totalCandidatures = await myApps.CountAsync();
        var enAttente = await myApps.CountAsync(a => a.Status == "Pending");
        var examinees = await myApps.CountAsync(a => a.Status == "Reviewed");
        var acceptees = await myApps.CountAsync(a => a.Status == "Accepted");
        var refusees = await myApps.CountAsync(a => a.Status == "Rejected");
        var entretiensPlanifies = await _context.Interviews.CountAsync(i => myOfferIds.Contains(i.Application.JobOfferId) && (i.Status == "Proposed" || i.Status == "Accepted"));
        var messagesNonLus = await _context.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead);

        var candidaturesParOffre = await myOffers
            .Select(j => new { label = j.Title.Length > 30 ? j.Title.Substring(0, 30) + "..." : j.Title, value = j.Applications.Count })
            .OrderByDescending(x => x.value)
            .Take(6)
            .ToListAsync();

        var candidaturesParStatut = new[] {
            new { label = "En attente", value = enAttente },
            new { label = "Examinees", value = examinees },
            new { label = "Acceptees", value = acceptees },
            new { label = "Refusees", value = refusees }
        };

        return new {
            totalOffres, offresActives, offresExpirees, totalCandidatures,
            enAttente, examinees, acceptees, refusees,
            entretiensPlanifies, messagesNonLus,
            candidaturesParOffre, candidaturesParStatut
        };
    }

    /// <summary>Stats pour le candidat connecte</summary>
    [HttpGet("stats/candidate")]
    [Authorize]
    public async Task<ActionResult<object>> CandidateStats()
    {
        var userId = GetUserId();
        var myApps = _context.Applications.Where(a => a.UserId == userId);

        var totalCandidatures = await myApps.CountAsync();
        var enAttente = await myApps.CountAsync(a => a.Status == "Pending");
        var examinees = await myApps.CountAsync(a => a.Status == "Reviewed");
        var acceptees = await myApps.CountAsync(a => a.Status == "Accepted");
        var refusees = await myApps.CountAsync(a => a.Status == "Rejected");
        var entretiens = await _context.Interviews.CountAsync(i => i.Application.UserId == userId && (i.Status == "Proposed" || i.Status == "Accepted"));
        var messagesNonLus = await _context.Messages.CountAsync(m => m.ReceiverId == userId && !m.IsRead);
        var favoris = 0; // client-side only
        var recherches = await _context.SavedSearches.CountAsync(s => s.UserId == userId);

        var candidaturesParStatut = new[] {
            new { label = "En attente", value = enAttente },
            new { label = "Examinees", value = examinees },
            new { label = "Acceptees", value = acceptees },
            new { label = "Refusees", value = refusees }
        };

        var dernieresCandidatures = await myApps
            .Include(a => a.JobOffer)
            .OrderByDescending(a => a.AppliedAt)
            .Take(5)
            .Select(a => new { a.Id, a.JobOfferId, titre = a.JobOffer.Title, entreprise = a.JobOffer.Company, a.Status, a.AppliedAt })
            .ToListAsync();

        return new {
            totalCandidatures, enAttente, examinees, acceptees, refusees,
            entretiens, messagesNonLus, recherches,
            candidaturesParStatut, dernieresCandidatures
        };
    }

    // ── Candidat ──

    /// <summary>Candidat : ranger ou sortir des archives une candidature.</summary>
    [HttpPatch("{id}/archive")]
    [Authorize]
    public async Task<IActionResult> SetArchived(int id, [FromBody] ApplicationArchiveDto dto)
    {
        var userId = GetUserId();
        var app = await _context.Applications.FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (app.UserId != userId) return Forbid();

        app.IsArchived = dto.IsArchived;
        await _context.SaveChangesAsync();
        return Ok(new { app.Id, app.IsArchived });
    }

    [HttpGet("track")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<object>>> TrackMyApplications()
    {
        var userId = GetUserId();
        var apps = await _context.Applications
            .Include(a => a.JobOffer)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new {
                a.Id, a.JobOfferId, a.FullName, a.Email, a.Phone, a.CoverLetter,
                a.ResumeUrl, a.Status, a.AppliedAt, a.UserId,
                a.IsArchived, a.ReviewedAt,
                // Exclure RecruiterNotes pour les candidats
                JobOffer = a.JobOffer
            })
            .ToListAsync();
        return Ok(apps);
    }

    /// <summary>Candidat : relancer le recruteur sur une candidature en attente.</summary>
    [HttpPost("{id}/remind")]
    [Authorize(Roles = "Candidate")]
    public async Task<IActionResult> Remind(int id)
    {
        var userId = GetUserId();
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (app.UserId != userId) return Forbid();
        if (app.Status != "Pending") return BadRequest(new { message = "Cette candidature a déjà été traitée." });

        var recruiterId = app.JobOffer?.CreatedByUserId;
        if (recruiterId == null) return BadRequest(new { message = "Recruteur introuvable." });

        var recent = await _context.Messages.AnyAsync(m => m.ApplicationId == id && m.SenderId == userId && m.CreatedAt >= DateTime.UtcNow.AddDays(-7));
        if (recent) return BadRequest(new { message = "Vous avez déjà relancé récemment. Patientez quelques jours." });

        _context.Messages.Add(new Message
        {
            SenderId = userId!, ReceiverId = recruiterId, ApplicationId = id,
            Content = $"Bonjour, je me permets de relancer concernant ma candidature au poste « {app.JobOffer!.Title} ». Restant à votre disposition pour tout complément.",
        });
        _context.Notifications.Add(new Notification
        {
            UserId = recruiterId,
            Title = "Relance d'un candidat",
            Message = $"{app.FullName} a relancé sa candidature à \"{app.JobOffer.Title}\".",
            Link = "/messagerie",
            Type = "Relance",
        });
        await _context.SaveChangesAsync();
        return Ok(new { message = "Relance envoyée au recruteur." });
    }

    [HttpPost]
    [EnableRateLimiting("publication")]
    [Authorize(Roles = "Candidate")]
    public async Task<ActionResult<Application>> Create(ApplicationCreateDto dto)
    {
        var userId = GetUserId()!;
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Unauthorized();

        var job = await _context.JobOffers.FirstOrDefaultAsync(j => j.Id == dto.JobOfferId && j.IsActive);
        if (job == null) return BadRequest("Offre introuvable ou inactive.");

        if (!string.IsNullOrEmpty(job.ExternalSource))
            return BadRequest(new { message = "Cette offre provient d'un site partenaire. Postulez directement sur le site d'origine." });

        if (job.IsDraft)
            return BadRequest(new { message = "Cette offre n'est pas encore publiée." });

        var resumeUrl = string.IsNullOrWhiteSpace(dto.ResumeUrl) ? user.ResumeUrl : dto.ResumeUrl;
        if (job.RequireResume && string.IsNullOrWhiteSpace(resumeUrl))
            return BadRequest(new { message = "Ce recruteur exige un CV : ajoutez-en un pour postuler." });

        if (HasMissingRequiredAnswers(job.ScreeningQuestions, dto.ScreeningAnswers))
            return BadRequest(new { message = "Répondez aux questions de présélection du recruteur pour postuler." });

        var alreadyApplied = await _context.Applications.AnyAsync(a => a.JobOfferId == dto.JobOfferId && a.UserId == userId);
        if (alreadyApplied) return BadRequest("Vous avez deja postule a cette offre.");

        // Check max applications limit
        var maxAppsStr = await _context.PlatformSettings
            .Where(s => s.Key == "max_applications_per_candidate")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        if (int.TryParse(maxAppsStr, out var maxApps) && maxApps > 0)
        {
            var currentCount = await _context.Applications.CountAsync(a => a.UserId == userId);
            if (currentCount >= maxApps)
                return BadRequest($"Vous avez atteint la limite de {maxApps} candidatures. Veuillez attendre qu'une de vos candidatures soit traitee.");
        }

        var app = new Application
        {
            JobOfferId = dto.JobOfferId,
            FullName = $"{user.FirstName} {user.LastName}",
            Email = user.Email!,
            Phone = dto.Phone ?? user.PhoneNumber,
            CoverLetter = dto.CoverLetter,
            ResumeUrl = resumeUrl,
            City = string.IsNullOrWhiteSpace(dto.City) ? user.City : dto.City,
            AvailableFrom = dto.AvailableFrom,
            SalaryExpectation = dto.SalaryExpectation,
            Source = string.IsNullOrWhiteSpace(dto.Source) ? "Plateforme" : dto.Source,
            ScreeningAnswers = dto.ScreeningAnswers,
            QualificationScore = ComputeQualificationScore(job.ScreeningQuestions, dto.ScreeningAnswers),
            UserId = userId
        };

        _context.Applications.Add(app);
        await _context.SaveChangesAsync();

        // Reponse automatique du recruteur au candidat (message dans la conversation)
        if (!string.IsNullOrWhiteSpace(job.AutoReplyMessage) && job.CreatedByUserId != null)
        {
            _context.Messages.Add(new Message
            {
                SenderId = job.CreatedByUserId,
                ReceiverId = userId,
                ApplicationId = app.Id,
                Content = job.AutoReplyMessage,
            });
            await _context.SaveChangesAsync();
        }

        // Notification au recruteur
        if (job.CreatedByUserId != null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = job.CreatedByUserId,
                Title = "Nouvelle candidature",
                Message = $"{user.FirstName} {user.LastName} a postule a votre offre \"{job.Title}\".",
                Link = "/admin/candidatures",
                Type = "NouveauCandidat"
            });
            await _context.SaveChangesAsync();

            // Real-time notification to recruiter
            foreach (var connId in ChatHub.GetConnectionIds(job.CreatedByUserId))
            {
                await _hubContext.Clients.Client(connId).SendAsync("NewApplication", new
                {
                    applicationId = app.Id,
                    candidateName = $"{user.FirstName} {user.LastName}",
                    jobTitle = job.Title
                });
                await _hubContext.Clients.Client(connId).SendAsync("NewNotification");
            }

            // Push notification to recruiter
            await _pushService.SendToUser(job.CreatedByUserId, "Nouvelle candidature",
                $"{user.FirstName} {user.LastName} a postule a \"{job.Title}\"",
                "/tabs/recruiter-applications");
        }

        // Adresse de reception choisie a la publication : si elle correspond a un
        // compte de la plateforme (assistant, boite de recrutement partagee),
        // cette personne est notifiee elle aussi.
        if (!string.IsNullOrWhiteSpace(job.ApplicationEmail))
        {
            var alt = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == job.ApplicationEmail && u.Id != job.CreatedByUserId);
            if (alt != null)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = alt.Id,
                    Title = "Nouvelle candidature",
                    Message = $"{user.FirstName} {user.LastName} a postule a l'offre \"{job.Title}\".",
                    Link = "/recruteur/candidatures",
                    Type = "NouveauCandidat"
                });
                await _context.SaveChangesAsync();

                foreach (var connId in ChatHub.GetConnectionIds(alt.Id))
                    await _hubContext.Clients.Client(connId).SendAsync("NewNotification");
            }
        }

        // ── Ce qui sort du site ──
        //
        // Tout ce qui precede — notification, temps reel, notification
        // poussee — suppose d'etre sur la plateforme, ou d'y revenir. Le
        // recruteur qui ne l'ouvre pas ignorait qu'on lui avait ecrit, et
        // le candidat n'avait aucune trace de sa demarche dans sa boite.
        //
        // Ces envois viennent apres l'enregistrement, et ne peuvent pas
        // l'annuler : voir « Prevenir ».
        await Prevenir(ModelesCourriel.CandidatureRecue(
            user.Email!, user.FirstName, job.Title, job.Company,
            $"{SiteUrl}/suivi"), "candidature recue");

        var recruteur = job.CreatedByUserId == null ? null
            : await _context.Users.FirstOrDefaultAsync(u => u.Id == job.CreatedByUserId);

        if (recruteur?.Email != null)
            await Prevenir(ModelesCourriel.NouvelleCandidature(
                recruteur.Email, recruteur.FirstName,
                $"{user.FirstName} {user.LastName}", job.Title,
                $"{SiteUrl}/recruteur/candidatures", app.City,
                // Le score ne veut rien dire sans questions : on ne le
                // montre que lorsqu'il en resume vraiment des reponses.
                string.IsNullOrWhiteSpace(job.ScreeningQuestions) ? null : app.QualificationScore),
                "nouvelle candidature");

        return CreatedAtAction(nameof(GetById), new { id = app.Id }, app);
    }
}
