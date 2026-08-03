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
[Route("api/candidate")]
[Authorize]
public class CandidateFeaturesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly AssistantIa _assistant;

    public CandidateFeaturesController(AppDbContext context, AssistantIa assistant)
    {
        _context = context;
        _assistant = assistant;
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ═══════════════════════════════════
    //  1. RETRAIT DE CANDIDATURE
    // ═══════════════════════════════════

    [HttpDelete("applications/{id}/withdraw")]
    public async Task<IActionResult> WithdrawApplication(int id)
    {
        var app = await _context.Applications.Include(a => a.JobOffer).FirstOrDefaultAsync(a => a.Id == id);
        if (app == null) return NotFound();
        if (app.UserId != UserId()) return Forbid();
        if (app.Status == "Accepted") return BadRequest("Impossible de retirer une candidature acceptee.");

        // Notify recruiter
        if (app.JobOffer.CreatedByUserId != null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = app.JobOffer.CreatedByUserId,
                Title = "Candidature retiree",
                Message = $"{app.FullName} a retire sa candidature pour \"{app.JobOffer.Title}\".",
                Link = "/admin/candidatures",
                Type = "CandidatureRetiree"
            });
        }

        _context.Applications.Remove(app);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══════════════════════════════════
    //  2. RECOMMANDATIONS
    // ═══════════════════════════════════
    //
    // La version precedente croisait les competences declarees avec les
    // etiquettes de l'offre et divisait par le nombre de competences du
    // candidat. Trois defauts, tous corriges par « Correspondance » :
    //
    //   Diviser par le nombre de competences declarees punissait le soin.
    //   Un candidat qui en saisit vingt n'atteignait jamais le seuil ;
    //   celui qui en saisissait deux le depassait toujours.
    //
    //   Un profil sans competences saisies — la majorite des inscrits —
    //   recevait un tableau vide. Pas un mauvais score : rien du tout,
    //   sans un mot d'explication. L'intitule du poste recherche et la
    //   ville suffisent pourtant a dire quelque chose.
    //
    //   « Location.Contains(City) » ignorait Canet-en-Roussillon a onze
    //   kilometres de Perpignan, et retenait Paris des que l'annonce
    //   citait Perpignan dans son texte.
    //
    // Le score reste calcule sans modele de langage : il doit tenir quand
    // la cle d'API manque, et il tourne sur des centaines d'offres.

    /// <summary>
    /// Combien d'offres on examine. Deux cents auparavant, c'est-a-dire
    /// les deux cents plus recentes et rien d'autre : sur un catalogue qui
    /// en compte des milliers, une offre parfaite publiee la semaine
    /// derniere n'etait jamais proposee. Le calcul coute des
    /// microsecondes par offre — c'est la lecture en base qui coute, d'ou
    /// une borne, mais elle peut etre bien plus haute.
    /// </summary>
    private const int OffresExaminees = 800;

    /// <summary>En deca, la proposition ferait perdre du temps.</summary>
    private const int ScoreMinimal = 35;

    [HttpGet("recommendations")]
    public async Task<ActionResult<IEnumerable<object>>> GetRecommendations()
    {
        var user = await _context.Users.FindAsync(UserId());
        if (user == null) return NotFound();

        var profil = Correspondance.Lire(user, await Souhaits());

        // Rien de connu sur le candidat : ni metier, ni competences, ni
        // ville. Il n'y a pas de recommandation honnete a faire, et en
        // inventer une serait pire que de n'en faire aucune.
        if (profil.Metier is null && profil.Competences.Count == 0 && profil.Position is null)
            return Ok(Array.Empty<object>());

        var dejaPostulees = await _context.Applications
            .Where(a => a.UserId == UserId())
            .Select(a => a.JobOfferId)
            .ToListAsync();

        var exclues = dejaPostulees.ToHashSet();

        var offres = await _context.JobOffers
            .AsNoTracking()
            .Where(j => j.IsActive && !j.IsDraft && j.ModerationStatus == "Approved")
            .OrderByDescending(j => j.CreatedAt)
            .Take(OffresExaminees)
            .ToListAsync();

        var retenues = offres
            .Where(j => !exclues.Contains(j.Id))
            .Select(j => new { Offre = j, Note = Correspondance.Noter(profil, j) })
            .Where(x => x.Note.Score >= ScoreMinimal)
            // A egalite de score, la plus recente : c'est celle dont le
            // poste est le plus probablement encore a pourvoir.
            .OrderByDescending(x => x.Note.Score)
            .ThenByDescending(x => x.Offre.CreatedAt)
            .Take(10)
            .Select(x => new
            {
                x.Offre.Id, x.Offre.Title, x.Offre.Company, x.Offre.Location,
                x.Offre.ContractType, x.Offre.Category, x.Offre.IsRemote,
                x.Offre.Salary, x.Offre.Tags, x.Offre.CreatedAt, x.Offre.IsUrgent,
                score = x.Note.Score,
                // La fiabilite dit sur quelle part des criteres le score a
                // pu etre etabli. Un pourcentage affiche sans elle est
                // peremptoire : « 90 % » sur une annonce qui ne dit ni son
                // salaire, ni l'experience attendue, ni le niveau de
                // formation ne repose que sur trois criteres.
                fiabilite = x.Note.Fiabilite,
                estimation = x.Note.Fiabilite < Correspondance.FiabiliteMinimale,
                raisons = x.Note.Raisons,
                reserves = x.Note.Reserves,
            })
            .ToList();

        return Ok(retenues);
    }

    /// <summary>
    /// Le rapprochement entre ce candidat et une offre precise.
    ///
    /// Sert la page de detail d'une offre, et elle seule. C'est le seul
    /// endroit du site ou une synthese redigee par le modele a un sens :
    /// une page, un appel. Sur une liste de vingt offres, la meme chose
    /// couterait vingt appels au premier affichage — le cache
    /// n'absorbant jamais la premiere visite.
    ///
    /// Le score, les raisons et les reserves, eux, sont calcules ici et
    /// maintenant, sans reseau. La synthese arrive en plus quand elle
    /// peut, et son absence ne se voit pas.
    /// </summary>
    [HttpGet("correspondance/{jobOfferId}")]
    public async Task<ActionResult<object>> GetCorrespondance(int jobOfferId, CancellationToken ct)
    {
        var user = await _context.Users.FindAsync(UserId());
        if (user == null) return NotFound();

        var offre = await _context.JobOffers.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobOfferId, ct);
        if (offre == null) return NotFound();

        var declarees = await AUneDeclaration();
        var note = Correspondance.Noter(user, offre, await Souhaits());

        // Rien de connu sur le candidat : mieux vaut n'afficher aucune
        // correspondance qu'un « 0 % » qui ressemble a un verdict.
        if (note.Fiabilite == 0)
            return Ok(new { applicable = false });

        string? resume = null;
        if (_assistant.Disponible)
            resume = await _assistant.Resumer(note, offre.Title, ct);

        return Ok(new
        {
            applicable = true,
            score = note.Score,
            fiabilite = note.Fiabilite,
            estimation = note.Fiabilite < Correspondance.FiabiliteMinimale,
            raisons = note.Raisons,
            reserves = note.Reserves,
            resume,
            // L'interface doit pouvoir distinguer une phrase generee d'un
            // critere calcule : les deux n'engagent pas de la meme facon.
            assiste = resume is not null,
            // D'ou viennent les souhaits qui ont pese dans ce score. Un
            // candidat qui ne reconnait pas son resultat doit pouvoir
            // remonter a ce qui l'a produit — et le corriger. Rendu ici
            // plutot que par un second appel : la page en ferait un de plus
            // sur chaque fiche d'offre, pour un seul mot.
            origineSouhaits = declarees ? "declarees" : "deduites",
        });
    }

    // ═══════════════════════════════════
    //  2 bis. PREFERENCES D'EMPLOI
    // ═══════════════════════════════════

    public sealed class PreferencesDto
    {
        [Range(0, 1_000_000, ErrorMessage = "Salaire annuel hors limites.")]
        public int? SalaireAnnuelMinimum { get; set; }

        [MaxLength(40)]
        public string? Contrat { get; set; }

        public bool? Distanciel { get; set; }

        [Range(1, 300, ErrorMessage = "Le rayon doit tenir entre 1 et 300 km.")]
        public int? RayonKm { get; set; }

        [MaxLength(12, ErrorMessage = "Douze métiers écartés au maximum.")]
        public List<string>? MetiersExclus { get; set; }
    }

    /// <summary>
    /// Ce que le candidat a declare chercher, et d'ou cela vient.
    ///
    /// « origine » n'est pas decoratif : l'interface doit pouvoir dire au
    /// candidat que sa correspondance repose sur une recherche qu'il a
    /// enregistree un soir, et non sur un choix. C'est la difference entre
    /// un resultat qu'on comprend et un resultat qu'on subit.
    /// </summary>
    [HttpGet("preferences")]
    public async Task<ActionResult<object>> GetPreferences()
    {
        var p = await _context.PreferencesEmploi.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == UserId());

        var declarees = p is not null && !p.EstVide;
        var effectifs = await Souhaits();

        return Ok(new
        {
            declarees,
            origine = declarees ? "declarees" : "deduites",
            salaireAnnuelMinimum = p?.SalaireAnnuelMinimum,
            contrat = p?.Contrat,
            distanciel = p?.Distanciel,
            rayonKm = p?.RayonKm,
            metiersExclus = p?.Exclus() ?? Array.Empty<string>(),
            misAJourLe = p?.MisAJourLe,
            // Le vocabulaire des familles connues : sans lui l'ecran ne
            // peut proposer que du texte libre, et deux orthographes de
            // « restauration » ne filtreraient pas la meme chose.
            metiersConnus = LexiqueMetiers.Familles,
            // Ce qui sert reellement au calcul aujourd'hui, declare ou
            // deduit : sans cela l'ecran ne peut pas expliquer un score.
            effectifs = new
            {
                salaireAnnuelMinimum = effectifs.SalaireAnnuelMinimum,
                contrat = effectifs.Contrat,
                distanciel = effectifs.Distanciel,
                rayonKm = effectifs.RayonKm,
            },
        });
    }

    /// <summary>Enregistrer ses preferences. Un champ nul veut dire « indifferent ».</summary>
    [HttpPut("preferences")]
    public async Task<ActionResult<object>> PutPreferences(PreferencesDto dto)
    {
        var p = await _context.PreferencesEmploi.FirstOrDefaultAsync(x => x.UserId == UserId());
        if (p is null)
        {
            p = new PreferencesEmploi { UserId = UserId() };
            _context.PreferencesEmploi.Add(p);
        }

        p.SalaireAnnuelMinimum = dto.SalaireAnnuelMinimum;
        // Une chaine vide venue d'un « select » vaut « indifferent », pas
        // un contrat nomme « ». Sans cela le moteur chercherait des offres
        // dont le type de contrat est la chaine vide, et n'en trouverait
        // aucune : le candidat verrait tous ses scores s'effondrer.
        p.Contrat = string.IsNullOrWhiteSpace(dto.Contrat) ? null : dto.Contrat.Trim();
        p.Distanciel = dto.Distanciel;
        p.RayonKm = dto.RayonKm;
        // Seules les familles que le lexique connait sont retenues : un
        // mot libre ne filtrerait rien, et le candidat croirait avoir
        // ecarte quelque chose.
        var exclus = (dto.MetiersExclus ?? new List<string>())
            .Select(m => m?.Trim() ?? "")
            .Where(m => m.Length > 0 && LexiqueMetiers.Familles.Contains(m))
            .Distinct()
            .ToList();
        p.MetiersExclus = exclus.Count == 0 ? null : string.Join(",", exclus);
        p.MisAJourLe = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return await GetPreferences();
    }

    // ═══════════════════════════════════
    //  2 ter. OFFRES ECARTEES
    // ═══════════════════════════════════

    /// <summary>Les offres que le candidat a ecartees.</summary>
    [HttpGet("offres-ecartees")]
    public async Task<ActionResult<IEnumerable<int>>> OffresEcartees() =>
        await _context.OffresEcartees.AsNoTracking()
            .Where(o => o.UserId == UserId())
            .Select(o => o.JobOfferId)
            .ToListAsync();

    /// <summary>
    /// Ecarter une offre de ses resultats.
    ///
    /// Rien n'en remonte au recruteur : c'est un geste de confort, pas un
    /// avis. Le dire ici parce que la tentation d'en faire un signal de
    /// qualite est reelle, et qu'elle transformerait un bouton anodin en
    /// jugement porte sur une annonce.
    /// </summary>
    [HttpPost("offres-ecartees/{jobOfferId:int}")]
    public async Task<IActionResult> Ecarter(int jobOfferId)
    {
        if (!await _context.JobOffers.AnyAsync(o => o.Id == jobOfferId)) return NotFound();

        var deja = await _context.OffresEcartees
            .AnyAsync(o => o.UserId == UserId() && o.JobOfferId == jobOfferId);
        if (!deja)
        {
            _context.OffresEcartees.Add(new OffreEcartee { UserId = UserId(), JobOfferId = jobOfferId });
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>Revenir sur un ecart : le geste doit etre reversible.</summary>
    [HttpDelete("offres-ecartees/{jobOfferId:int}")]
    public async Task<IActionResult> Reprendre(int jobOfferId)
    {
        var ligne = await _context.OffresEcartees
            .FirstOrDefaultAsync(o => o.UserId == UserId() && o.JobOfferId == jobOfferId);
        if (ligne != null)
        {
            _context.OffresEcartees.Remove(ligne);
            await _context.SaveChangesAsync();
        }
        return NoContent();
    }

    /// <summary>
    /// Ce que le candidat cherche, et que sa fiche ne dit pas.
    ///
    /// Les preferences declarees font foi. A defaut — et c'est encore le
    /// cas de la plupart des comptes — on retombe sur la derniere
    /// recherche enregistree : une recherche qu'on prend la peine de
    /// garder est une declaration d'intention, et c'est mieux que rien.
    ///
    /// Le repli ne se declenche que sur des preferences absentes ou
    /// entierement vides. Un candidat qui a ouvert le formulaire pour n'y
    /// mettre qu'un salaire plancher a dit quelque chose : lui ajouter par
    /// deduction un contrat qu'il n'a pas choisi reviendrait a inventer.
    /// </summary>
    private async Task<Souhaits> Souhaits()
    {
        var p = await _context.PreferencesEmploi.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == UserId());

        if (p is not null && !p.EstVide)
            return new Souhaits(p.Contrat, p.Distanciel, p.SalaireAnnuelMinimum, p.RayonKm);

        return await Deduits();
    }

    /// <summary>Le candidat a-t-il renseigne au moins un critere ?</summary>
    private async Task<bool> AUneDeclaration()
    {
        var p = await _context.PreferencesEmploi.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == UserId());
        return p is not null && !p.EstVide;
    }

    /// <summary>Le repli : ce que la derniere recherche enregistree laisse deviner.</summary>
    private async Task<Souhaits> Deduits()
    {

        var derniere = await _context.SavedSearches
            .AsNoTracking()
            .Where(r => r.UserId == UserId())
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (derniere is null) return new Souhaits();

        return new Souhaits(
            Contrat: derniere.ContractType,
            Distanciel: derniere.IsRemote);
    }

    // ═══════════════════════════════════
    //  3. NOTES SUR LES OFFRES
    // ═══════════════════════════════════

    [HttpGet("notes")]
    public async Task<ActionResult<IEnumerable<JobNote>>> GetMyNotes()
    {
        return await _context.JobNotes
            .Where(n => n.UserId == UserId())
            .Include(n => n.JobOffer)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();
    }

    [HttpGet("notes/{jobId}")]
    public async Task<ActionResult<object>> GetNote(int jobId)
    {
        var note = await _context.JobNotes.FirstOrDefaultAsync(n => n.UserId == UserId() && n.JobOfferId == jobId);
        return note != null ? new { note.Id, note.Content, note.UpdatedAt } : new { Id = 0, Content = "", UpdatedAt = DateTime.MinValue };
    }

    [HttpPut("notes/{jobId}")]
    public async Task<IActionResult> SaveNote(int jobId, [FromBody] NoteDto dto)
    {
        var uid = UserId();
        var note = await _context.JobNotes.FirstOrDefaultAsync(n => n.UserId == uid && n.JobOfferId == jobId);

        if (string.IsNullOrWhiteSpace(dto.Content))
        {
            if (note != null) { _context.JobNotes.Remove(note); await _context.SaveChangesAsync(); }
            return NoContent();
        }

        if (note != null)
        {
            note.Content = dto.Content;
            note.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.JobNotes.Add(new JobNote { UserId = uid, JobOfferId = jobId, Content = dto.Content });
        }
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══════════════════════════════════
    //  4. PROPOSITION DE CRENEAUX
    // ═══════════════════════════════════

    [HttpPatch("interviews/{id}/propose-slots")]
    public async Task<IActionResult> ProposeSlots(int id, [FromBody] ProposeSlotsDto dto)
    {
        var interview = await _context.Interviews
            .Include(i => i.Application).ThenInclude(a => a.JobOffer)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (interview == null) return NotFound();
        if (interview.Application.UserId != UserId()) return Forbid();
        if (interview.Status != "Proposed") return BadRequest("Vous ne pouvez proposer des creneaux que pour un entretien en attente.");

        interview.CandidateSlots = System.Text.Json.JsonSerializer.Serialize(dto.Slots);
        interview.CandidateMessage = dto.Message;
        interview.Status = "Negotiating";
        await _context.SaveChangesAsync();

        // Notify recruiter
        if (interview.Application.JobOffer.CreatedByUserId != null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = interview.Application.JobOffer.CreatedByUserId,
                Title = "Creneaux proposes",
                Message = $"{interview.Application.FullName} a propose des creneaux alternatifs pour l'entretien \"{interview.Application.JobOffer.Title}\".",
                Link = "/entretiens",
                Type = "Entretien"
            });
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    // ═══════════════════════════════════
    //  5. DASHBOARD ANALYTIQUE ENRICHI
    // ═══════════════════════════════════

    [HttpGet("analytics")]
    public async Task<ActionResult<object>> GetAnalytics()
    {
        var uid = UserId();
        var apps = await _context.Applications
            .Where(a => a.UserId == uid)
            .Include(a => a.JobOffer)
            .ToListAsync();

        var total = apps.Count;
        var withResponse = apps.Count(a => a.Status != "Pending");
        var responseRate = total > 0 ? Math.Round((double)withResponse / total * 100) : 0;

        // Average response time (days between AppliedAt and first status change)
        // We approximate: reviewed/accepted/rejected apps have been responded to
        var respondedApps = apps.Where(a => a.Status != "Pending").ToList();

        // Applications by month (last 6 months)
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var appsByMonth = apps
            .Where(a => a.AppliedAt >= sixMonthsAgo)
            .GroupBy(a => new { a.AppliedAt.Year, a.AppliedAt.Month })
            .Select(g => new { label = $"{g.Key.Month:D2}/{g.Key.Year}", value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // Applications by category
        var appsByCategory = apps
            .GroupBy(a => a.JobOffer?.Category ?? "Autre")
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Applications by contract type
        var appsByContract = apps
            .GroupBy(a => a.JobOffer?.ContractType ?? "Autre")
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Status breakdown
        var statusBreakdown = apps
            .GroupBy(a => a.Status)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToList();

        // Interviews count
        var interviewCount = await _context.Interviews
            .CountAsync(i => i.Application.UserId == uid);

        var acceptedCount = apps.Count(a => a.Status == "Accepted");
        var conversionRate = total > 0 ? Math.Round((double)acceptedCount / total * 100) : 0;

        return new
        {
            total, responseRate, conversionRate, interviewCount,
            appsByMonth, appsByCategory, appsByContract, statusBreakdown,
        };
    }

    // ═══════════════════════════════════
    //  6. ALERTES EMPLOI
    // ═══════════════════════════════════

    [HttpPatch("saved-searches/{id}/toggle-alert")]
    public async Task<IActionResult> ToggleAlert(int id)
    {
        var search = await _context.SavedSearches.FindAsync(id);
        if (search == null || search.UserId != UserId()) return NotFound();
        search.AlertEnabled = !search.AlertEnabled;
        if (search.AlertEnabled) search.LastAlertAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { search.Id, search.AlertEnabled });
    }

    [HttpGet("check-alerts")]
    public async Task<ActionResult<object>> CheckAlerts()
    {
        var uid = UserId();
        var searches = await _context.SavedSearches
            .Where(s => s.UserId == uid && s.AlertEnabled)
            .ToListAsync();

        var newOffers = new List<object>();
        foreach (var s in searches)
        {
            var since = s.LastAlertAt ?? s.CreatedAt;
            var query = _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved" && j.CreatedAt > since);
            if (!string.IsNullOrEmpty(s.Query)) query = query.Where(j => j.Title.Contains(s.Query) || j.Company.Contains(s.Query));
            if (!string.IsNullOrEmpty(s.Category)) query = query.Where(j => j.Category == s.Category);
            if (!string.IsNullOrEmpty(s.ContractType)) query = query.Where(j => j.ContractType == s.ContractType);
            if (s.IsRemote.HasValue) query = query.Where(j => j.IsRemote == s.IsRemote.Value);
            if (!string.IsNullOrEmpty(s.Location)) query = query.Where(j => j.Location.Contains(s.Location));

            var count = await query.CountAsync();
            if (count > 0)
            {
                newOffers.Add(new { searchId = s.Id, searchLabel = s.Label, newCount = count });
                s.LastAlertAt = DateTime.UtcNow;
            }
        }
        await _context.SaveChangesAsync();
        return new { alerts = newOffers };
    }
}

// DTOs
public class NoteDto
{
    [StringLength(Limites.Texte, ErrorMessage = "Cette note ne peut pas dépasser 20 000 caractères.")]
    public string? Content { get; set; }
}

public class ProposeSlotsDto
{
    // Une liste bornee : proposer dix mille creneaux n'a aucun sens pour
    // un entretien, et chacun d'eux devient une ligne en base.
    [MaxLength(20, ErrorMessage = "Vous ne pouvez pas proposer plus de 20 créneaux.")]
    public List<string> Slots { get; set; } = new(); // ISO date strings

    [Longueur(Limites.Paragraphe)]
    public string? Message { get; set; }
}
