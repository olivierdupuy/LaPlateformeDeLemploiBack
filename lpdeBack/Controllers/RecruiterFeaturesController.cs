using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Services;
using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/recruiter")]
[Authorize(Roles = "Admin,Recruiter")]
public class RecruiterFeaturesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly PerimetreRecruteur _perimetre;

    public RecruiterFeaturesController(AppDbContext context, PerimetreRecruteur perimetre)
    {
        _context = context;
        _perimetre = perimetre;
    }
    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool IsAdmin() => User.IsInRole("Admin");

    // ═══════════════════════════════════
    //  1. DUPLICATION D'OFFRES
    // ═══════════════════════════════════

    [HttpPost("offers/{id}/duplicate")]
    public async Task<ActionResult<JobOffer>> DuplicateOffer(int id)
    {
        var src = await _context.JobOffers.FindAsync(id);
        if (src == null) return NotFound();
        if (!IsAdmin() && src.CreatedByUserId != UserId()) return Forbid();

        var durationStr = await _context.PlatformSettings
            .Where(s => s.Key == "default_offer_duration").Select(s => s.Value).FirstOrDefaultAsync();
        var duration = int.TryParse(durationStr, out var d) ? d : 30;

        // La copie part en brouillon : elle porte « (copie) » dans son titre et
        // attend d'etre relue dans le tunnel de publication avant d'etre
        // proposee aux candidats.
        var dup = new JobOffer
        {
            Title = src.Title + " (copie)",
            Company = src.Company,
            Location = src.Location,
            Address = src.Address,
            WorkplaceType = src.WorkplaceType,
            Description = src.Description,
            ContractType = src.ContractType,
            ContractDuration = src.ContractDuration,
            WorkSchedule = src.WorkSchedule,
            HoursPerWeek = src.HoursPerWeek,
            StartDate = src.StartDate,
            Openings = src.Openings,
            Salary = src.Salary,
            SalaryPeriod = src.SalaryPeriod,
            SupplementalPay = src.SupplementalPay,
            Category = src.Category,
            IsRemote = src.IsRemote,
            Tags = src.Tags,
            MinSalary = src.MinSalary,
            MaxSalary = src.MaxSalary,
            ExperienceRequired = src.ExperienceRequired,
            EducationLevel = src.EducationLevel,
            Languages = src.Languages,
            Benefits = src.Benefits,
            CompanyDescription = src.CompanyDescription,
            CompanyLogoUrl = src.CompanyLogoUrl,
            IsUrgent = src.IsUrgent,
            EasyApply = src.EasyApply,
            RequireResume = src.RequireResume,
            ApplicationEmail = src.ApplicationEmail,
            ScreeningQuestions = src.ScreeningQuestions,
            AutoReplyMessage = src.AutoReplyMessage,
            Latitude = src.Latitude,
            Longitude = src.Longitude,
            CreatedByUserId = UserId(),
            ExpiresAt = DateTime.UtcNow.AddDays(duration),
            ModerationStatus = "Approved",
            IsDraft = true,
            IsActive = false,
        };

        _context.JobOffers.Add(dup);
        await _context.SaveChangesAsync();
        return Ok(dup);
    }

    // ═══════════════════════════════════
    //  2. RECHERCHE AVANCEE DE CANDIDATS
    // ═══════════════════════════════════

    [HttpGet("candidates/search")]
    public async Task<ActionResult<IEnumerable<object>>> SearchCandidates(
        [FromQuery] string? search,
        [FromQuery] string? skills,
        [FromQuery] string? city,
        [FromQuery] int? minExperience,
        [FromQuery] int? maxExperience,
        [FromQuery] string? education,
        [FromQuery] bool? disponible,
        [FromQuery] string? sort)
    {
        // « IsSearchable » manquait a cette condition. Le profil promet
        // « Vous n'apparaissez dans aucune recherche de candidats », et
        // ce point d'entree-ci l'ignorait — l'autre vivier, celui de
        // « CandidatesController », le respectait depuis toujours. Un
        // candidat qui s'etait masque restait donc visible d'un ecran sur
        // deux, sans qu'aucune erreur ne le signale.
        var query = _context.Users
            .Where(u => u.Role == "Candidate" && u.IsActive && u.IsSearchable)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.FirstName.Contains(search) || u.LastName.Contains(search) || (u.Bio != null && u.Bio.Contains(search)) || (u.Title != null && u.Title.Contains(search)));

        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(u => u.City != null && u.City.Contains(city));

        if (minExperience.HasValue)
            query = query.Where(u => u.ExperienceYears.HasValue && u.ExperienceYears >= minExperience.Value);

        if (maxExperience.HasValue)
            query = query.Where(u => u.ExperienceYears.HasValue && u.ExperienceYears <= maxExperience.Value);

        if (!string.IsNullOrWhiteSpace(education))
            query = query.Where(u => u.Education != null && u.Education.Contains(education));

        if (!string.IsNullOrWhiteSpace(skills))
        {
            var skillList = skills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var skill in skillList)
                query = query.Where(u => u.Skills != null && u.Skills.Contains(skill));
        }

        // Disponible aujourd'hui : la date est passee ou c'est aujourd'hui.
        // Un candidat qui n'a rien declare n'est pas « indisponible » — il
        // n'a rien dit, et le filtre l'ecarte sans le juger.
        var aujourdhui = DateTime.UtcNow.Date;
        if (disponible == true)
            query = query.Where(u => u.DisponibleLe != null && u.DisponibleLe <= aujourdhui);

        query = sort switch
        {
            // Les disponibles d'abord, du plus tot au plus tard ; ceux qui
            // n'ont rien dit ferment la liste plutot que de la fausser.
            "disponibilite" => query.OrderBy(u => u.DisponibleLe == null ? 1 : 0).ThenBy(u => u.DisponibleLe),
            "experience_desc" => query.OrderByDescending(u => u.ExperienceYears ?? 0),
            "experience_asc" => query.OrderBy(u => u.ExperienceYears ?? 0),
            "name" => query.OrderBy(u => u.LastName),
            _ => query.OrderByDescending(u => u.CreatedAt),
        };

        var candidates = await query.Take(100).ToListAsync();

        // Un compte de candidatures par profil, c'etait cent requetes pour
        // une page. Un seul regroupement les remplace.
        var ids = candidates.Select(c => c.Id).ToList();
        var comptes = await _context.Applications
            .Where(a => a.UserId != null && ids.Contains(a.UserId))
            .GroupBy(a => a.UserId!)
            .Select(g => new { UserId = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.N);

        var result = candidates.Select(c => new
        {
            c.Id, c.FirstName, c.LastName, c.AvatarUrl, c.Title, c.Skills,
            c.ExperienceYears, c.Education, c.City, c.Bio, c.CreatedAt,
            applicationCount = comptes.GetValueOrDefault(c.Id),
            c.DisponibleLe,
            disponibleMaintenant = c.DisponibleLe != null && c.DisponibleLe <= aujourdhui,
        }).ToList();

        return Ok(result);
    }

    // ═══════════════════════════════════
    //  3. TEMPLATES DE MESSAGES
    // ═══════════════════════════════════

    [HttpGet("templates")]
    public async Task<ActionResult<IEnumerable<MessageTemplate>>> GetTemplates()
    {
        var uid = UserId();
        // Include system defaults (userId null) + user's own
        return await _context.MessageTemplates
            .Where(t => t.UserId == uid)
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .ToListAsync();
    }

    [HttpPost("templates")]
    public async Task<ActionResult<MessageTemplate>> CreateTemplate(TemplateDto dto)
    {
        var template = new MessageTemplate
        {
            UserId = UserId(),
            Name = dto.Name,
            Content = dto.Content,
            Category = dto.Category ?? "General",
        };
        _context.MessageTemplates.Add(template);
        await _context.SaveChangesAsync();
        return Ok(template);
    }

    [HttpPut("templates/{id}")]
    public async Task<IActionResult> UpdateTemplate(int id, TemplateDto dto)
    {
        var t = await _context.MessageTemplates.FindAsync(id);
        if (t == null || t.UserId != UserId()) return NotFound();
        t.Name = dto.Name;
        t.Content = dto.Content;
        t.Category = dto.Category ?? t.Category;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("templates/{id}")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        var t = await _context.MessageTemplates.FindAsync(id);
        if (t == null || t.UserId != UserId()) return NotFound();
        _context.MessageTemplates.Remove(t);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══════════════════════════════════
    //  4. STATS PAR OFFRE
    // ═══════════════════════════════════

    [HttpGet("offers/{id}/stats")]
    public async Task<ActionResult<object>> GetOfferStats(int id)
    {
        var job = await _context.JobOffers.Include(j => j.Applications).FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();
        if (!IsAdmin() && job.CreatedByUserId != UserId()) return Forbid();

        var apps = job.Applications.ToList();
        var interviews = await _context.Interviews
            .Where(i => apps.Select(a => a.Id).Contains(i.ApplicationId))
            .ToListAsync();

        return new
        {
            job.Id, job.Title, job.ViewCount,
            totalApplications = apps.Count,
            pending = apps.Count(a => a.Status == "Pending"),
            reviewed = apps.Count(a => a.Status == "Reviewed"),
            accepted = apps.Count(a => a.Status == "Accepted"),
            rejected = apps.Count(a => a.Status == "Rejected"),
            totalInterviews = interviews.Count,
            conversionRate = job.ViewCount > 0 ? Math.Round((double)apps.Count / job.ViewCount * 100, 1) : 0,
            appsByDay = apps
                .Where(a => a.AppliedAt >= DateTime.UtcNow.AddDays(-30))
                .GroupBy(a => a.AppliedAt.Date)
                .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
                .OrderBy(x => x.label).ToList(),
            funnel = new[] {
                new { label = "Vues", value = job.ViewCount },
                new { label = "Candidatures", value = apps.Count },
                new { label = "Entretiens", value = interviews.Count },
                new { label = "Acceptees", value = apps.Count(a => a.Status == "Accepted") },
            },
        };
    }

    // ═══════════════════════════════════
    //  2 bis. INVITER UN PROFIL A POSTULER
    // ═══════════════════════════════════

    /// <summary>
    /// Inviter un candidat du vivier a postuler sur une de ses offres.
    ///
    /// Le vivier permettait de trouver quelqu'un et de le regarder. Pour
    /// lui parler, il fallait passer par la messagerie, hors de toute
    /// offre : le candidat recevait « votre profil m'interesse » sans
    /// savoir pour quel poste.
    ///
    /// L'invitation reste une proposition. Le candidat garde la main, et
    /// son silence n'est pas compte comme un refus.
    /// </summary>
    [HttpPost("invitations")]
    public async Task<ActionResult<object>> Inviter(InvitationDto dto)
    {
        var offre = await _context.JobOffers.FindAsync(dto.JobOfferId);
        if (offre == null) return NotFound(new { message = "Offre introuvable." });
        if (!IsAdmin() && !await _perimetre.PeutGerer(UserId(), offre.CreatedByUserId)) return Forbid();

        // Inviter sur une annonce que le public ne voit pas enverrait le
        // candidat vers une page vide.
        if (!offre.IsActive || offre.IsDraft || offre.ModerationStatus != "Approved")
            return BadRequest(new { message = "Cette offre n'est pas en ligne." });

        var candidat = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.CandidatId);
        if (candidat == null || candidat.Role != "Candidate")
            return NotFound(new { message = "Candidat introuvable." });

        // Le vivier respecte « IsSearchable » ; l'invitation doit le
        // respecter aussi, sans quoi un identifiant devine suffirait a
        // contourner le masquage.
        if (!candidat.IsSearchable || !candidat.IsActive)
            return BadRequest(new { message = "Ce profil n'est pas visible des recruteurs." });

        if (await _context.Applications.AnyAsync(a => a.JobOfferId == dto.JobOfferId && a.UserId == dto.CandidatId))
            return BadRequest(new { message = "Cette personne a déjà postulé à cette offre." });

        if (await _context.Invitations.AnyAsync(i => i.JobOfferId == dto.JobOfferId && i.CandidatId == dto.CandidatId))
            return BadRequest(new { message = "Cette personne a déjà été invitée sur cette offre." });

        var invitation = new Invitation
        {
            JobOfferId = dto.JobOfferId,
            CandidatId = dto.CandidatId,
            RecruteurId = UserId(),
            Message = string.IsNullOrWhiteSpace(dto.Message) ? null : dto.Message.Trim(),
        };
        _context.Invitations.Add(invitation);

        _context.Notifications.Add(new Notification
        {
            UserId = dto.CandidatId,
            Title = "Une entreprise vous invite à postuler",
            Message = $"{offre.Company} vous propose de postuler au poste de « {offre.Title} ».",
            Link = "/suivi?onglet=invitations",
            Type = "Invitation",
        });

        await _context.SaveChangesAsync();
        return Ok(new { invitation.Id, invitation.EnvoyeeLe });
    }

    /// <summary>Les invitations envoyees par l'equipe, et ce qu'elles sont devenues.</summary>
    [HttpGet("invitations")]
    public async Task<ActionResult<IEnumerable<object>>> InvitationsEnvoyees()
    {
        var visibles = await OffresGerables();

        return await _context.Invitations.AsNoTracking()
            .Where(i => visibles.Contains(i.JobOfferId))
            .OrderByDescending(i => i.EnvoyeeLe)
            .Join(_context.Users, i => i.CandidatId, u => u.Id, (i, u) => new
            {
                i.Id, i.JobOfferId, i.EnvoyeeLe, i.VueLe, i.Reponse, i.ReponduLe,
                poste = i.JobOffer.Title,
                candidat = u.FirstName + " " + u.LastName,
                candidatId = u.Id,
            })
            .Take(200)
            .ToListAsync();
    }

    // ═══════════════════════════════════
    //  4 bis. ETIQUETTES SUR LES OFFRES
    // ═══════════════════════════════════

    /// <summary>
    /// Les etiquettes de toutes les offres que l'appelant peut gerer.
    ///
    /// Rendues a part et non dans la charge utile des offres : le point
    /// d'entree public rend l'entite « JobOffer » entiere, et une
    /// etiquette interne — « priorite direction », « a revoir » — n'a rien
    /// a faire chez un visiteur du catalogue. La separation n'est pas une
    /// commodite, c'est ce qui empeche la fuite.
    ///
    /// « vocabulaire » sert a proposer les mots deja employes : sans lui,
    /// une equipe se retrouve avec « campagne ete », « Campagne Été » et
    /// « campagne-ete » pour la meme chose.
    /// </summary>
    [HttpGet("offers/etiquettes")]
    public async Task<ActionResult<object>> Etiquettes()
    {
        var visibles = await OffresGerables();

        var lignes = await _context.EtiquettesOffre.AsNoTracking()
            .Where(e => visibles.Contains(e.JobOfferId))
            .OrderBy(e => e.Nom)
            .Select(e => new { e.JobOfferId, e.Nom, e.Cle })
            .ToListAsync();

        return Ok(new
        {
            parOffre = lignes.GroupBy(l => l.JobOfferId)
                             .ToDictionary(g => g.Key.ToString(), g => g.Select(x => x.Nom).ToArray()),
            vocabulaire = lignes.GroupBy(l => l.Cle)
                                .Select(g => g.First().Nom)
                                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                                .ToArray(),
        });
    }

    /// <summary>
    /// Remplace les etiquettes d'une offre. La liste envoyee fait foi.
    ///
    /// Remplacer plutot qu'ajouter : l'ecran montre la liste entiere et
    /// l'envoie entiere. Un point d'entree qui ajouterait obligerait a un
    /// second pour retirer, et les deux se desynchroniseraient au premier
    /// double clic.
    /// </summary>
    [HttpPut("offers/{id}/etiquettes")]
    public async Task<ActionResult<object>> PoserEtiquettes(int id, EtiquettesDto dto)
    {
        var offre = await _context.JobOffers.FindAsync(id);
        if (offre == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(UserId(), offre.CreatedByUserId)) return Forbid();

        // Replier avant de dedoublonner : « Urgent » et « urgent » ne
        // doivent pas passer tous les deux, sans quoi le filtre en perdrait
        // la moitie.
        var voulues = (dto.Etiquettes ?? new List<string>())
            .Select(n => n?.Trim() ?? "")
            .Where(n => n.Length > 0)
            .GroupBy(EtiquetteOffre.Replier)
            .Select(g => g.First())
            .Take(EtiquettesDto.Maximum)
            .ToList();

        var existantes = await _context.EtiquettesOffre.Where(e => e.JobOfferId == id).ToListAsync();
        var clefsVoulues = voulues.Select(EtiquetteOffre.Replier).ToHashSet();

        _context.EtiquettesOffre.RemoveRange(existantes.Where(e => !clefsVoulues.Contains(e.Cle)));

        var clefsPresentes = existantes.Select(e => e.Cle).ToHashSet();
        foreach (var nom in voulues.Where(n => !clefsPresentes.Contains(EtiquetteOffre.Replier(n))))
        {
            _context.EtiquettesOffre.Add(new EtiquetteOffre
            {
                JobOfferId = id,
                Nom = nom,
                Cle = EtiquetteOffre.Replier(nom),
                CreeParUserId = UserId(),
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { id, etiquettes = voulues });
    }

    /// <summary>Les offres que l'appelant peut gerer, en identifiants.</summary>
    private async Task<List<int>> OffresGerables()
    {
        if (IsAdmin())
            return await _context.JobOffers.Select(o => o.Id).ToListAsync();

        var equipe = await _perimetre.Equipe(UserId());
        return await _context.JobOffers
            .Where(o => o.CreatedByUserId != null && equipe.Contains(o.CreatedByUserId))
            .Select(o => o.Id)
            .ToListAsync();
    }

    // ═══════════════════════════════════
    //  5. ACTIONS GROUPEES
    // ═══════════════════════════════════

    [HttpPatch("applications/bulk-status")]
    public async Task<IActionResult> BulkUpdateStatus(BulkStatusDto dto)
    {
        if (!StatutCandidature.Existe(dto.Status)) return BadRequest("Statut invalide.");

        var uid = UserId();
        var apps = await _context.Applications
            .Include(a => a.JobOffer)
            .Where(a => dto.Ids.Contains(a.Id))
            .ToListAsync();

        // Le partage d'equipe s'applique ici comme ailleurs. Ce controle
        // comparait l'auteur de l'offre a l'appelant, sans passer par le
        // perimetre : un recruteur pouvait traiter la candidature d'un
        // collegue une par une, et pas en lot. Rien ne l'annoncait — la
        // ligne etait simplement ignoree.
        var traitees = 0;
        foreach (var app in apps)
        {
            if (!IsAdmin() && !await _perimetre.PeutGerer(uid, app.JobOffer.CreatedByUserId)) continue;

            if (app.ReviewedAt == null && dto.Status != StatutCandidature.EnAttente)
                app.ReviewedAt = DateTime.UtcNow;
            app.Status = dto.Status;
            traitees++;
        }

        await _context.SaveChangesAsync();

        // Le compte rendu portait le nombre de candidatures LUES, pas
        // modifiees : un recruteur qui en selectionnait douze dont trois
        // lui revenaient lisait « 12 mises a jour » et croyait les neuf
        // autres traitees.
        return Ok(new { updated = traitees, demandees = apps.Count });
    }

    /// <summary>
    /// Ouvrir, suspendre ou fermer plusieurs offres d'un geste.
    ///
    /// Le pendant de l'action groupee sur les candidatures, qui existait
    /// deja : une campagne de recrutement se suspend rarement offre par
    /// offre.
    /// </summary>
    [HttpPatch("offers/bulk-etat")]
    public async Task<IActionResult> BulkEtatOffres(BulkEtatOffreDto dto)
    {
        if (!EtatOffre.Existe(dto.Etat)) return BadRequest(new { message = "État inconnu." });

        var uid = UserId();
        var offres = await _context.JobOffers
            .Where(o => dto.Ids.Contains(o.Id))
            .ToListAsync();

        var traitees = 0;
        var ignorees = 0;
        foreach (var o in offres)
        {
            if (!IsAdmin() && !await _perimetre.PeutGerer(uid, o.CreatedByUserId)) continue;

            // Memes garde-fous qu'a l'unite : un brouillon n'a pas d'etat
            // de publication, et rouvrir une offre non approuvee
            // contournerait la moderation. Elles sont comptees a part
            // plutot que silencieusement sautees.
            if (o.IsDraft || (dto.Etat == EtatOffre.Ouverte && o.ModerationStatus != "Approved"))
            {
                ignorees++;
                continue;
            }

            EtatOffre.Appliquer(o, dto.Etat);
            traitees++;
        }

        await _context.SaveChangesAsync();
        return Ok(new { updated = traitees, ignorees, demandees = offres.Count });
    }
}

// DTOs
public class TemplateDto
{
    [Required(ErrorMessage = "Donnez un nom à ce modèle.")]
    [StringLength(Limites.Ligne, MinimumLength = 2)]
    [SansBalisage]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Écrivez le contenu du modèle.")]
    [StringLength(Limites.Texte, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;

    [Longueur(Limites.Nom), SansBalisage]
    public string? Category { get; set; }
}

public class InvitationDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Offre inconnue.")]
    public int JobOfferId { get; set; }

    [Required(ErrorMessage = "Indiquez le candidat.")]
    [MaxLength(450)]
    public string CandidatId { get; set; } = string.Empty;

    [Longueur(1000), SansBalisage]
    public string? Message { get; set; }
}

public class EtiquettesDto
{
    /// <summary>
    /// Huit suffit largement, et la borne evite qu'un collage maladroit
    /// pose deux cents mots sur une offre.
    /// </summary>
    public const int Maximum = 8;

    [MaxLength(Maximum, ErrorMessage = "Huit étiquettes au maximum par offre.")]
    public List<string>? Etiquettes { get; set; }
}

public class BulkEtatOffreDto
{
    [MinLength(1, ErrorMessage = "Sélectionnez au moins une offre.")]
    [MaxLength(200, ErrorMessage = "Traitez au maximum 200 offres à la fois.")]
    public List<int> Ids { get; set; } = new();

    [Required(ErrorMessage = "Indiquez l'état.")]
    [EtatOffre]
    public string Etat { get; set; } = string.Empty;
}

public class BulkStatusDto
{
    // Le lot est borne : chaque element declenche un courriel au candidat.
    // Sans borne, un seul appel peut en expedier des milliers, et rien ne
    // les rattrape une fois partis.
    [MinLength(1, ErrorMessage = "Sélectionnez au moins une candidature.")]
    [MaxLength(500, ErrorMessage = "Traitez au maximum 500 candidatures à la fois.")]
    public List<int> Ids { get; set; } = new();

    [Required(ErrorMessage = "Indiquez le statut.")]
    [StatutCandidature]
    public string Status { get; set; } = string.Empty;
}
