using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Services;
using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.Controllers;

/// <summary>La lettre d'information, cote console.</summary>
[ApiController]
[Route("api/admin/newsletter")]
[Authorize(Roles = "Admin")]
public class AdminNewsletterController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly NewsletterService _lettre;
    private readonly BrevoService _brevo;
    private readonly ActivityLogService _log;
    private readonly LettreEnBlocs _blocs;

    public AdminNewsletterController(AppDbContext context, NewsletterService lettre,
                                     BrevoService brevo, ActivityLogService log,
                                     LettreEnBlocs blocs)
    {
        _context = context;
        _lettre = lettre;
        _brevo = brevo;
        _log = log;
        _blocs = blocs;
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private string NomComplet() =>
        $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}".Trim();

    // ═══════════════════════════════════════════
    //  1. ABONNES
    // ═══════════════════════════════════════════

    [HttpGet("abonnes")]
    public async Task<ActionResult<object>> Abonnes(
        [FromQuery] string? q, [FromQuery] string? statut, [FromQuery] string? source,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var requete = _context.NewsletterSubscribers.Include(s => s.User).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            requete = requete.Where(s => s.Email.Contains(t)
                                      || (s.FirstName != null && s.FirstName.Contains(t))
                                      || (s.LastName != null && s.LastName.Contains(t))
                                      || (s.City != null && s.City.Contains(t)));
        }
        if (!string.IsNullOrWhiteSpace(statut)) requete = requete.Where(s => s.Status == statut);
        if (!string.IsNullOrWhiteSpace(source)) requete = requete.Where(s => s.Source == source);

        var total = await requete.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var items = await requete
            .OrderByDescending(s => s.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(s => new
            {
                s.Id, s.Email, s.FirstName, s.LastName, s.Status, s.Source,
                s.City, s.Department, s.Categories,
                s.CreatedAt, s.ConfirmedAt, s.UnsubscribedAt, s.LastSentAt,
                s.ConsentAt, s.ConsentIp, s.ConsecutiveFailures,
                role = s.User != null ? s.User.Role : null,
                membre = s.UserId != null,
            })
            .ToListAsync(ct);

        // Les facettes valent mieux qu'un total seul : « 1 240 abonnes »
        // ne dit pas combien attendent encore de confirmer.
        var facettes = new
        {
            total = await _context.NewsletterSubscribers.CountAsync(ct),
            confirmes = await _context.NewsletterSubscribers.CountAsync(s => s.Status == "Confirmed", ct),
            enAttente = await _context.NewsletterSubscribers.CountAsync(s => s.Status == "Pending", ct),
            desabonnes = await _context.NewsletterSubscribers.CountAsync(s => s.Status == "Unsubscribed", ct),
            injoignables = await _context.NewsletterSubscribers.CountAsync(s => s.Status == "Bounced", ct),
            membres = await _context.NewsletterSubscribers.CountAsync(s => s.UserId != null, ct),
        };

        return Ok(new { items, total, page, pageSize, facettes });
    }

    /// <summary>Export CSV des abonnes joignables — pour une sauvegarde, ou un autre outil.</summary>
    [HttpGet("abonnes/export")]
    public async Task<IActionResult> Exporter(CancellationToken ct)
    {
        var lignes = await _context.NewsletterSubscribers
            .Where(s => s.Status == "Confirmed" && s.UnsubscribedAt == null)
            .OrderBy(s => s.Email)
            .Select(s => new { s.Email, s.FirstName, s.LastName, s.City, s.Categories, s.ConfirmedAt, s.Source })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("email;prenom;nom;ville;categories;confirme_le;origine");
        foreach (var l in lignes)
            sb.AppendLine(string.Join(';', new[]
            {
                l.Email, l.FirstName, l.LastName, l.City, l.Categories,
                l.ConfirmedAt?.ToString("yyyy-MM-dd"), l.Source,
            }.Select(Csv)));

        // BOM : sans lui, Excel lit « Ã© » la ou il y a « é ».
        var octets = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(octets, "text/csv", $"abonnes-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    private static string Csv(string? v) =>
        string.IsNullOrEmpty(v) ? "" : v.Contains(';') || v.Contains('"') || v.Contains('\n')
            ? $"\"{v.Replace("\"", "\"\"")}\"" : v;

    /// <summary>Desabonner quelqu'un depuis la console, a sa demande.</summary>
    [HttpPost("abonnes/{id:int}/desabonner")]
    public async Task<IActionResult> DesabonnerDepuisLaConsole(int id, CancellationToken ct)
    {
        var a = await _context.NewsletterSubscribers.FindAsync(new object[] { id }, ct);
        if (a == null) return NotFound();

        a.Status = "Unsubscribed";
        a.UnsubscribedAt = DateTime.UtcNow;
        a.UnsubscribeReason = "Retire par l'administration";
        await _context.SaveChangesAsync(ct);

        await _log.Log("NewsletterDesabonnement", "Newsletter", id,
            $"{a.Email} retire de la lettre d'information", UserId(), NomComplet(),
            SessionService.Ip(HttpContext));

        return Ok(new { message = "Cette adresse ne recevra plus la lettre d'information." });
    }

    // ═══════════════════════════════════════════
    //  2. CAMPAGNES
    // ═══════════════════════════════════════════

    [HttpGet("campagnes")]
    public async Task<ActionResult<object>> Campagnes(CancellationToken ct)
        => Ok(await _context.NewsletterCampaigns
            .OrderByDescending(c => c.Id)
            .Select(c => new
            {
                c.Id, c.Subject, c.PreviewText, c.Status, c.CreatedAt, c.SentAt,
                c.Recipients, c.Delivered, c.Failed, c.CreatedByName,
                c.SegmentRoles, c.SegmentCategories, c.SegmentCities,
                c.SegmentDepartments, c.SegmentActivity,
                enCours = c.Status == "Sending",
                restants = c.Status == "Sending"
                    ? c.Deliveries.Count(d => d.Status == "Pending") : 0,
            })
            .ToListAsync(ct));

    [HttpGet("campagnes/{id:int}")]
    public async Task<ActionResult<object>> Campagne(int id, CancellationToken ct)
    {
        var c = await _context.NewsletterCampaigns.FindAsync(new object[] { id }, ct);
        if (c == null) return NotFound();

        return Ok(new
        {
            c.Id, c.Subject, c.PreviewText, c.BodyHtml, c.Blocs, c.Status,
            c.SegmentRoles, c.SegmentCategories, c.SegmentCities,
            c.SegmentDepartments, c.SegmentActivity,
            c.CreatedAt, c.SentAt, c.Recipients, c.Delivered, c.Failed, c.CreatedByName,
            restants = await _context.NewsletterDeliveries
                .CountAsync(d => d.CampaignId == id && d.Status == "Pending", ct),
            echecs = await _context.NewsletterDeliveries
                .Where(d => d.CampaignId == id && d.Status == "Failed")
                .OrderByDescending(d => d.Id).Take(30)
                .Select(d => new { d.Email, d.Error })
                .ToListAsync(ct),
        });
    }

    [HttpPost("campagnes")]
    public async Task<ActionResult<object>> Creer([FromBody] CampagneDto dto, CancellationToken ct)
    {
        var c = new NewsletterCampaign
        {
            Subject = (dto.Subject ?? "").Trim(),
            PreviewText = dto.PreviewText,
            BodyHtml = dto.BodyHtml ?? "",
            Blocs = dto.Blocs,
            CreatedByUserId = UserId(),
            CreatedByName = NomComplet(),
        };
        Appliquer(c, dto);
        _context.NewsletterCampaigns.Add(c);
        await _context.SaveChangesAsync(ct);
        return Ok(new { c.Id });
    }

    [HttpPut("campagnes/{id:int}")]
    public async Task<IActionResult> Modifier(int id, [FromBody] CampagneDto dto, CancellationToken ct)
    {
        var c = await _context.NewsletterCampaigns.FindAsync(new object[] { id }, ct);
        if (c == null) return NotFound();

        // Une campagne partie ne se retouche pas : ses statistiques
        // deviendraient incomprehensibles, et l'on ne saurait plus a quoi
        // les gens ont repondu.
        if (c.Status != "Draft")
            return Conflict(new { message = "Cette campagne est deja partie : son contenu ne se modifie plus. Dupliquez-la pour en ecrire une nouvelle." });

        c.Subject = (dto.Subject ?? "").Trim();
        c.PreviewText = dto.PreviewText;
        c.BodyHtml = dto.BodyHtml ?? "";
        c.Blocs = dto.Blocs;
        Appliquer(c, dto);
        await _context.SaveChangesAsync(ct);
        return Ok(new { message = "Brouillon enregistre." });
    }

    private static void Appliquer(NewsletterCampaign c, CampagneDto dto)
    {
        c.SegmentRoles = Joindre(dto.Roles);
        c.SegmentCategories = Joindre(dto.Categories);
        c.SegmentCities = Joindre(dto.Cities);
        c.SegmentDepartments = Joindre(dto.Departments);
        c.SegmentActivity = string.IsNullOrWhiteSpace(dto.Activity) ? null : dto.Activity;
    }

    private static string? Joindre(List<string>? v) =>
        v == null || v.Count == 0 ? null : string.Join(',', v.Select(x => x.Trim()).Where(x => x.Length > 0));

    [HttpDelete("campagnes/{id:int}")]
    public async Task<IActionResult> Supprimer(int id, CancellationToken ct)
    {
        var c = await _context.NewsletterCampaigns.FindAsync(new object[] { id }, ct);
        if (c == null) return NotFound();
        if (c.Status == "Sending")
            return Conflict(new { message = "Cette campagne est en cours d'envoi. Arretez-la d'abord." });

        _context.NewsletterCampaigns.Remove(c);
        await _context.SaveChangesAsync(ct);
        return Ok(new { message = "Campagne supprimee." });
    }

    /// <summary>Combien de personnes recevront, avec ce segment. Compte en direct.</summary>
    [HttpPost("campagnes/destinataires")]
    public async Task<ActionResult<object>> Compter([FromBody] CampagneDto dto, CancellationToken ct)
    {
        var temoin = new NewsletterCampaign();
        Appliquer(temoin, dto);
        var n = await _lettre.CompterDestinataires(temoin, ct);

        return Ok(new
        {
            destinataires = n,
            // Un segment vide n'est pas une erreur : c'est un envoi a personne,
            // et il vaut mieux le dire avant qu'apres.
            message = n == 0
                ? "Aucun abonne ne correspond a ce ciblage. Elargissez-le, ou verifiez que vos abonnes ont bien confirme."
                : n == 1 ? "Une personne recevra ce message."
                : $"{n} personnes recevront ce message.",
        });
    }

    /// <summary>
    /// L'apercu, rendu sur un abonne reel quand il en existe un.
    ///
    /// Sur un abonne fictif, on ne verrait jamais qu'un champ de fusion
    /// vide casse une phrase — « Bonjour , » avec sa virgule orpheline.
    /// </summary>
    [HttpPost("campagnes/apercu")]
    public async Task<ActionResult<object>> Apercu(
        [FromBody] CampagneDto dto, [FromQuery] int? abonneId, CancellationToken ct)
    {
        var temoin = new NewsletterCampaign
        {
            Subject = dto.Subject ?? "",
            PreviewText = dto.PreviewText,
            BodyHtml = dto.BodyHtml ?? "",
            Blocs = dto.Blocs,
        };
        Appliquer(temoin, dto);

        var vises = _lettre.Destinataires(temoin);

        // ── Dans la peau de qui ──
        //
        // Un bloc d'offres « pour chaque abonne » ne montre rien sur un
        // destinataire fictif : c'est la ville et les centres d'interet de
        // quelqu'un de reel qui font la selection. L'apercu s'incarne donc
        // dans un abonne, et la console laisse en changer — c'est le seul
        // moyen de verifier qu'une lettre personnalisee tient debout pour
        // plusieurs personnes, et pas seulement pour la premiere.
        //
        // A defaut de choix, on prend le mieux renseigne plutot que le
        // premier venu : sur un abonne sans prenom, l'apercu montre
        // « Bonjour , » et l'on croit a un defaut du gabarit. Les manques
        // se disent plus bas, chiffres.
        var abonne = abonneId is int id
            ? await vises.FirstOrDefaultAsync(s => s.Id == id, ct)
            : null;

        abonne ??= await vises
            .OrderByDescending(s => (s.FirstName != null ? 1 : 0)
                                  + (s.LastName != null ? 1 : 0)
                                  + (s.City != null ? 1 : 0)
                                  + (s.Categories != null ? 1 : 0))
            .FirstOrDefaultAsync(ct)
            ?? new NewsletterSubscriber
            {
                Email = "exemple@destinataire.fr",
                FirstName = "Camille",
                LastName = "Fontaine",
                City = "34 - Montpellier",
                Department = "34",
                UnsubscribeToken = "apercu",
            };

        var offres = string.IsNullOrWhiteSpace(temoin.Blocs)
            ? ContexteOffres.Vide
            : await _blocs.Preparer(LettreEnBlocs.Lire(temoin.Blocs), ct);

        var (html, texte) = _lettre.Composer(temoin, abonne, offres);

        // Quelques abonnes a proposer, pris parmi les mieux renseignes et
        // dans des villes differentes : incarner l'apercu dans trois
        // personnes du meme departement n'apprendrait rien.
        var incarnations = await vises
            .OrderByDescending(s => (s.City != null ? 2 : 0) + (s.Categories != null ? 1 : 0))
            .Take(40)
            .Select(s => new { s.Id, s.Email, s.FirstName, s.City, s.Categories })
            .ToListAsync(ct);

        // ── Ce qui manquera chez certains ──
        // Un champ de fusion vide ne casse pas l'envoi : il laisse un trou
        // dans la phrase, et personne ne s'en apercoit avant que trois mille
        // messages soient partis avec « Bonjour , ». On compte donc, pour
        // chaque champ employe, combien de destinataires ne l'ont pas.
        var gabarit = (temoin.Subject ?? "") + (temoin.BodyHtml ?? "") + (temoin.Blocs ?? "");
        var total = await vises.CountAsync(ct);
        var lacunes = new List<object>();

        if (total > 0)
        {
            if (gabarit.Contains("{{prenom}}"))
            {
                var n = await vises.CountAsync(s => s.FirstName == null || s.FirstName == "", ct);
                if (n > 0) lacunes.Add(new { champ = "prenom", manquant = n, total });
            }
            if (gabarit.Contains("{{nom}}"))
            {
                var n = await vises.CountAsync(s => s.LastName == null || s.LastName == "", ct);
                if (n > 0) lacunes.Add(new { champ = "nom", manquant = n, total });
            }
            if (gabarit.Contains("{{ville}}"))
            {
                var n = await vises.CountAsync(s => s.City == null || s.City == "", ct);
                if (n > 0) lacunes.Add(new { champ = "ville", manquant = n, total });
            }
        }

        return Ok(new
        {
            sujet = _lettre.Rendre(temoin.Subject, abonne, html: false),
            html,
            texte,
            rendu = abonne.Id > 0
                ? $"rendu sur un abonne reel ({abonne.Email})"
                : "rendu sur un destinataire fictif : aucun abonne ne correspond encore a ce ciblage",
            lacunes,
            // De qui il s'agit, et qui d'autre on peut incarner.
            abonne = new { abonne.Id, abonne.Email, abonne.FirstName, abonne.City, abonne.Categories },
            incarnations,
        });
    }

    // ═══════════════════════════════════════════
    //  Le catalogue, pour le bloc « offres »
    // ═══════════════════════════════════════════

    /// <summary>
    /// De quoi remplir un bloc d'offres a la main.
    ///
    /// Deux usages dans le meme point d'entree : chercher une offre a
    /// ajouter, et relire celles deja choisies. Le second compte autant
    /// que le premier — sans lui, l'editeur afficherait une liste
    /// d'identifiants nus, et une offre retiree du catalogue depuis
    /// resterait dans la lettre sans que personne le voie.
    ///
    /// La recherche passe par « RequeteLibre », comme celle du site : le
    /// redacteur tape « alternance developpeur perpignan » et obtient ce
    /// qu'un candidat obtiendrait.
    /// </summary>
    [HttpGet("offres")]
    public async Task<ActionResult<object>> Offres(
        [FromQuery] string? q, [FromQuery] string? ids, CancellationToken ct)
    {
        var actives = _context.JobOffers.AsNoTracking()
            .Where(j => j.IsActive && !j.IsDraft && j.ModerationStatus == "Approved");

        // ── Relire une selection ──
        if (!string.IsNullOrWhiteSpace(ids))
        {
            var voulus = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var n) ? n : 0)
                .Where(n => n > 0)
                .Take(LettreEnBlocs.MaxOffresParBloc)
                .ToList();

            var trouvees = await actives.Where(j => voulus.Contains(j.Id))
                .Select(j => new { j.Id, j.Title, j.Company, j.Location, j.ContractType, j.Salary, j.CreatedAt })
                .ToListAsync(ct);

            // Dans l'ordre du redacteur, et non celui de la base : c'est son
            // classement, il ne doit pas se defaire tout seul.
            return Ok(voulus.Select(id => trouvees.FirstOrDefault(j => j.Id == id))
                            .Where(j => j is not null).ToList());
        }

        // ── Chercher ──
        var requete = RequeteLibre.Analyser(q);

        if (!string.IsNullOrWhiteSpace(requete.Contrat))
        {
            var contrat = requete.Contrat;
            actives = actives.Where(j => j.ContractType == contrat);
        }

        if (requete.MotsClefs.Count > 0)
        {
            var mots = requete.MotsClefs.Take(4).ToList();
            actives = actives.Where(j => mots.Any(m =>
                j.Title.Contains(m) || j.Company.Contains(m) || (j.Tags != null && j.Tags.Contains(m))));
        }

        if (!string.IsNullOrWhiteSpace(requete.Lieu))
        {
            var lieu = requete.Lieu;
            actives = actives.Where(j => j.Location.Contains(lieu));
        }

        var candidates = await actives
            .OrderByDescending(j => j.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var classees = candidates
            .OrderByDescending(j => RequeteLibre.Pertinence(requete, j))
            .ThenByDescending(j => j.CreatedAt)
            .Take(25)
            .Select(j => new { j.Id, j.Title, j.Company, j.Location, j.ContractType, j.Salary, j.CreatedAt });

        return Ok(classees);
    }

    /// <summary>Les familles de metiers, pour le bloc d'offres en mode « recherche ».</summary>
    [HttpGet("metiers")]
    public ActionResult<object> Metiers() => Ok(LexiqueMetiers.Familles);

    /// <summary>Un envoi d'essai a soi-meme, avant d'ecrire a tout le monde.</summary>
    [HttpPost("campagnes/{id:int}/essai")]
    public async Task<ActionResult<object>> Essai(int id, [FromBody] EssaiCampagneDto? dto, CancellationToken ct)
    {
        var c = await _context.NewsletterCampaigns.FindAsync(new object[] { id }, ct);
        if (c == null) return NotFound();

        var destinataire = string.IsNullOrWhiteSpace(dto?.Email)
            ? User.FindFirstValue(ClaimTypes.Email) : dto!.Email;
        if (string.IsNullOrWhiteSpace(destinataire))
            return BadRequest(new { message = "Aucune adresse de destination." });

        // L'essai part sur l'administrateur lui-meme, qui n'est pas abonne
        // et n'a donc ni ville ni centres d'interet. Un bloc d'offres
        // « pour chaque abonne » n'aurait rien a lui proposer : c'est le
        // repli qui joue, et c'est justement ce qu'il faut verifier — un
        // essai qui montre un bloc vide annonce une lettre trouee.
        var temoin = new NewsletterSubscriber
        {
            Email = destinataire,
            FirstName = User.FindFirstValue(ClaimTypes.GivenName),
            LastName = User.FindFirstValue(ClaimTypes.Surname),
            UnsubscribeToken = "essai",
        };

        var offres = string.IsNullOrWhiteSpace(c.Blocs)
            ? ContexteOffres.Vide
            : await _blocs.Preparer(LettreEnBlocs.Lire(c.Blocs), ct);

        var (html, texte) = _lettre.Composer(c, temoin, offres);
        var r = await _brevo.Envoyer(destinataire, null,
            "[Essai] " + _lettre.Rendre(c.Subject, temoin, html: false), html, texte, null, ct);

        return Ok(new
        {
            parti = r.Parti,
            message = r.Parti
                ? $"Essai expedie a {destinataire}."
                : r.Erreur ?? "L'essai n'est pas parti.",
        });
    }

    /// <summary>
    /// Le depart.
    ///
    /// Les lignes de livraison sont ecrites ici, avant le premier envoi :
    /// c'est ce qui rend la campagne reprenable apres un arret, et ce qui
    /// empeche d'ecrire deux fois a la meme personne. Le service de fond
    /// s'en charge ensuite.
    /// </summary>
    [HttpPost("campagnes/{id:int}/envoyer")]
    public async Task<ActionResult<object>> Envoyer(int id, CancellationToken ct)
    {
        var c = await _context.NewsletterCampaigns.FindAsync(new object[] { id }, ct);
        if (c == null) return NotFound();
        if (c.Status != "Draft")
            return Conflict(new { message = "Cette campagne n'est plus un brouillon." });
        // Un contenu, sous l'une ou l'autre forme : les blocs pour une
        // lettre ecrite dans l'editeur, le HTML pour celles d'avant. Ne
        // verifier que « BodyHtml » refuserait desormais toute campagne
        // faite de blocs, c'est-a-dire toutes les nouvelles.
        var aUnContenu = !string.IsNullOrWhiteSpace(c.BodyHtml)
                         || LettreEnBlocs.Lire(c.Blocs).Count > 0;

        if (string.IsNullOrWhiteSpace(c.Subject) || !aUnContenu)
            return BadRequest(new { message = "Un objet et un contenu sont necessaires." });
        if (!_brevo.EstConfigure)
            return StatusCode(StatusCodes.Status501NotImplemented,
                new { message = "Aucune cle Brevo n'est configuree : les campagnes ne peuvent pas partir." });

        // Verifier avant de partir plutot que de constater apres : une
        // campagne lancee vers un expediteur non valide echoue destinataire
        // par destinataire, et il faut lire les journaux pour comprendre.
        var diag = await _brevo.Interroger(ct);
        if (diag.Joignable && !diag.ExpediteurValide)
            return BadRequest(new
            {
                message = $"L'adresse « {_brevo.Expediteur} » n'est pas validee chez Brevo : " +
                          "tous les envois seraient refuses. Ajoutez-la dans « Expediteurs & domaines » " +
                          "avant de lancer la campagne.",
            });

        var abonnes = await _lettre.Destinataires(c).Select(s => new { s.Id, s.Email }).ToListAsync(ct);
        if (abonnes.Count == 0)
            return BadRequest(new { message = "Aucun abonne ne correspond a ce ciblage." });

        foreach (var a in abonnes)
            _context.NewsletterDeliveries.Add(new NewsletterDelivery
            {
                CampaignId = c.Id, SubscriberId = a.Id, Email = a.Email, Status = "Pending",
            });

        c.Recipients = abonnes.Count;
        c.Status = "Sending";
        await _context.SaveChangesAsync(ct);

        // Le quota ne bloque pas : ce qui passe part, le reste attend le
        // lendemain et sera repris. Mais on le dit, sinon la campagne
        // semble s'arreter sans raison a trois cents messages.
        var alerte = diag.CreditsRestants is { } credits && credits < abonnes.Count
            ? $" Attention : il ne reste que {credits} credit(s) chez Brevo. Les {abonnes.Count - credits} " +
              "derniers messages attendront le renouvellement du quota."
            : "";

        await _log.Log("NewsletterEnvoi", "Newsletter", c.Id,
            $"Campagne « {c.Subject} » lancee vers {abonnes.Count} destinataire(s)",
            UserId(), NomComplet(), SessionService.Ip(HttpContext));

        return Ok(new
        {
            message = $"Envoi lance vers {abonnes.Count} destinataire(s). Il se poursuit en arriere-plan : vous pouvez quitter cette page." + alerte,
            destinataires = abonnes.Count,
        });
    }

    /// <summary>Arrete un envoi en cours. Ce qui est parti est parti.</summary>
    [HttpPost("campagnes/{id:int}/arreter")]
    public async Task<ActionResult<object>> Arreter(int id, CancellationToken ct)
    {
        var c = await _context.NewsletterCampaigns.FindAsync(new object[] { id }, ct);
        if (c == null) return NotFound();
        if (c.Status != "Sending")
            return Conflict(new { message = "Cette campagne n'est pas en cours d'envoi." });

        var restants = await _context.NewsletterDeliveries
            .Where(d => d.CampaignId == id && d.Status == "Pending")
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.Status, "Failed")
                                      .SetProperty(x => x.Error, "Envoi interrompu"), ct);

        c.Status = "Sent";
        c.SentAt = DateTime.UtcNow;
        c.Delivered = await _context.NewsletterDeliveries.CountAsync(d => d.CampaignId == id && d.Status == "Sent", ct);
        c.Failed = await _context.NewsletterDeliveries.CountAsync(d => d.CampaignId == id && d.Status == "Failed", ct);
        await _context.SaveChangesAsync(ct);

        await _log.Log("NewsletterArret", "Newsletter", c.Id,
            $"Campagne « {c.Subject} » interrompue, {restants} envoi(s) annule(s)",
            UserId(), NomComplet(), SessionService.Ip(HttpContext));

        return Ok(new { message = $"Envoi arrete. {c.Delivered} message(s) etaient deja partis ; {restants} ont ete annules." });
    }

    // ═══════════════════════════════════════════
    //  3. ETAT DU SERVICE
    // ═══════════════════════════════════════════

    /// <summary>
    /// L'etat du service, interroge chez Brevo et non devine.
    ///
    /// Deux pannes ne se voient autrement qu'apres coup, dans les journaux,
    /// une fois la campagne partie et perdue : un expediteur que Brevo n'a
    /// pas valide, et un quota epuise. Les deux se lisent d'un appel.
    /// </summary>
    [HttpGet("etat")]
    public async Task<ActionResult<object>> Etat(CancellationToken ct)
    {
        var d = await _brevo.Interroger(ct);
        var abonnes = await _context.NewsletterSubscribers
            .CountAsync(s => s.Status == "Confirmed" && s.UnsubscribedAt == null, ct);

        var avertissements = new List<string>();
        if (d.Joignable && !d.ExpediteurValide)
            avertissements.Add(
                $"L'adresse « {_brevo.Expediteur} » n'est pas un expediteur valide chez Brevo : " +
                "chaque envoi sera refuse. Ajoutez-la dans « Expediteurs & domaines » et confirmez " +
                "le courriel de validation." +
                (d.ExpediteursActifs.Count > 0
                    ? $" Adresses actuellement valides : {string.Join(", ", d.ExpediteursActifs)}."
                    : ""));

        if (d.CreditsRestants is { } credits && credits < abonnes)
            avertissements.Add(
                $"Il reste {credits} credit(s) chez Brevo pour {abonnes} abonne(s) : une campagne " +
                "vers tout le monde s'arreterait en chemin. L'offre gratuite plafonne a trois cents " +
                "envois par jour — ciblez plus etroitement, ou etalez sur plusieurs jours.");

        return Ok(new
        {
            configure = _brevo.EstConfigure,
            etat = _brevo.Etat,
            joignable = d.Joignable,
            compte = d.Compte,
            creditsRestants = d.CreditsRestants,
            expediteur = _brevo.Expediteur,
            expediteurValide = d.ExpediteurValide,
            expediteursActifs = d.ExpediteursActifs,
            abonnes,
            avertissements,
            erreur = d.Erreur,
            consequence = !_brevo.EstConfigure
                ? "Aucune campagne ne peut partir. Les abonnements et les desinscriptions continuent de fonctionner : seule l'expedition est a l'arret."
                : avertissements.Count == 0
                    ? "Les campagnes partent normalement."
                    : "Les campagnes ne partiront pas correctement en l'etat.",
            champs = NewsletterService.Champs.Select(c => new { cle = c.Cle, description = c.Description }),
        });
    }
}

public class CampagneDto
{
    // Pas « obligatoire » ici, et ce n'est pas un oubli : ce meme objet
    // sert a compter les destinataires d'un ciblage et a en calculer
    // l'apercu, ou aucun objet n'est encore saisi — et un brouillon
    // s'enregistre justement avant d'etre redige. L'exigence porte la ou
    // elle a un sens : au moment d'expedier, ou l'envoi est refuse sans
    // objet ni corps.
    [Longueur(Limites.Ligne), SansBalisage]
    public string? Subject { get; set; }

    [Longueur(Limites.Ligne)]
    public string? PreviewText { get; set; }

    // Le corps est du HTML redige dans la console : c'est le seul champ
    // ou le balisage est l'objet meme du champ. Il reste borne — un
    // courriel de deux megaoctets serait refuse par les messageries
    // avant meme d'atteindre une boite.
    [StringLength(200_000, ErrorMessage = "Le message est trop volumineux : les messageries le rejetteraient.")]
    public string? BodyHtml { get; set; }

    /// <summary>
    /// La lettre en blocs. Prend le pas sur « BodyHtml » quand elle est
    /// renseignee ; celui-ci reste pour les campagnes ecrites avant
    /// l'editeur, qui doivent continuer de partir.
    ///
    /// Le texte des blocs est echappe au rendu : le champ n'a donc pas
    /// besoin d'interdire le balisage, il ne sera jamais interprete.
    /// </summary>
    [StringLength(200_000, ErrorMessage = "Le message est trop volumineux : les messageries le rejetteraient.")]
    public string? Blocs { get; set; }

    public List<string>? Roles { get; set; }
    public List<string>? Categories { get; set; }
    public List<string>? Cities { get; set; }
    public List<string>? Departments { get; set; }

    [Longueur(Limites.Nom), SansBalisage]
    public string? Activity { get; set; }
}

public class EssaiCampagneDto
{
    [AdresseCourriel]
    public string? Email { get; set; }
}
