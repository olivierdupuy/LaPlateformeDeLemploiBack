using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.DTOs;
using lpdeBack.Hubs;
using lpdeBack.Services;

using lpdeBack.Validation;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobOffersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly PerimetreRecruteur _perimetre;
    private readonly UserManager<AppUser> _userManager;
    private readonly IMemoryCache _cache;
    private readonly lpdeBack.Services.FacturationService _facturation;
    private readonly lpdeBack.Services.QualiteCatalogue _qualite;
    private readonly lpdeBack.Services.AssistantIa _assistant;

    /// <summary>
    /// En deca de combien d'offres trouvees dans les colonnes courtes on
    /// va fouiller les descriptions. Une page standard : au-dessus, le
    /// candidat a deja de quoi lire, et les offres que la description
    /// aurait ajoutees sortiraient de toute facon en fin de classement.
    /// </summary>
    private const int SeuilDescriptions = 24;

    public JobOffersController(AppDbContext context, UserManager<AppUser> userManager, IMemoryCache cache, PerimetreRecruteur perimetre,
                               lpdeBack.Services.FacturationService facturation, lpdeBack.Services.QualiteCatalogue qualite,
                               lpdeBack.Services.AssistantIa assistant)
    {
        _perimetre = perimetre;
        _context = context;
        _userManager = userManager;
        _cache = cache;
        _facturation = facturation;
        _qualite = qualite;
        _assistant = assistant;
    }

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin() => User.IsInRole("Admin");

    // ── Public endpoints (no auth) ──

    [HttpGet]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? contractType,
        [FromQuery] bool? isRemote,
        [FromQuery] string? location,
        [FromQuery] int? salaryMin,
        [FromQuery] int? salaryMax,
        [FromQuery] string? experience,
        [FromQuery] string? education,
        [FromQuery] string? workSchedule,
        [FromQuery] string? languages,
        [FromQuery] string? benefits,
        [FromQuery] int? datePosted,
        [FromQuery] int? radius,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 24 : pageSize;
        // Auto-expire offers past their expiration date
        await _context.JobOffers
            .Where(j => j.IsActive && j.ExpiresAt != null && j.ExpiresAt < DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.IsActive, false));

        var query = _context.JobOffers.Where(j => j.IsActive && !j.IsDraft && j.ModerationStatus == "Approved").AsQueryable();

        // ── Lecture de la recherche ──
        //
        // « developpeur react alternance perpignan » partait entier dans
        // « Title.Contains(...) » et ne ramenait rien : aucun intitule
        // d'offre au monde ne contient cette phrase. Le candidat voyait
        // une page vide, sans un mot d'explication, et en concluait qu'il
        // n'y avait pas de travail.
        //
        // On en tire donc des filtres, et on ne cherche en plein texte
        // que ce qui reste. Les filtres explicites de l'URL l'emportent
        // toujours : ils viennent des cases que le candidat a cochees,
        // la lecture de sa phrase n'est qu'une deduction.
        var requete = RequeteLibre.Analyser(search);

        // Les termes a chercher en plein texte. Un OU entre les mots, pas
        // un ET : « developpeur react » doit ramener les annonces de
        // developpeur meme quand elles ne citent pas React, quitte a les
        // classer derriere. Le tri par pertinence, plus bas, fait le reste.
        //
        // Quand la phrase n'a rien donne d'exploitable, on la cherche
        // entiere plutot que de renvoyer le catalogue.
        var termes = requete.MotsClefs.Count > 0
            ? requete.MotsClefs.Take(6).ToList()
            : !string.IsNullOrWhiteSpace(search) && !requete.ADesFiltres
                ? new List<string> { search.Trim() }
                : new List<string>();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(j => j.Category == category);

        var contratRetenu = !string.IsNullOrWhiteSpace(contractType) ? contractType : requete.Contrat;
        if (!string.IsNullOrWhiteSpace(contratRetenu))
            query = query.Where(j => j.ContractType == contratRetenu);

        var distanciel = isRemote ?? requete.Distanciel;
        if (distanciel.HasValue)
            query = query.Where(j => j.IsRemote == distanciel.Value);

        // Recherche par rayon : si un rayon + un lieu geocodable sont fournis, on filtre par distance
        // (plus bas, apres materialisation) au lieu d'un simple Contains sur le libelle.
        // Le lieu et le rayon peuvent aussi venir de la phrase — « autour
        // de Perpignan » vaut « location=Perpignan&radius=25 » — mais le
        // parametre explicite garde la main.
        var lieuRetenu = !string.IsNullOrWhiteSpace(location) ? location : requete.Lieu;
        var rayonRetenu = radius ?? requete.RayonKm;

        (double Lat, double Lng)? center = null;
        var useRadius = rayonRetenu.HasValue && rayonRetenu.Value > 0 && !string.IsNullOrWhiteSpace(lieuRetenu);
        if (useRadius) center = lpdeBack.Services.GeoUtils.Geocode(lieuRetenu);

        if (!string.IsNullOrWhiteSpace(lieuRetenu) && !(useRadius && center != null))
            query = query.Where(j => j.Location.Contains(lieuRetenu));

        var salairePlancher = salaryMin ?? requete.SalaireAnnuelMinimum;
        if (salairePlancher.HasValue)
            query = query.Where(j => j.MaxSalary >= salairePlancher.Value || (j.MinSalary.HasValue && j.MinSalary >= salairePlancher.Value));

        if (salaryMax.HasValue)
            query = query.Where(j => j.MinSalary <= salaryMax.Value || (j.MaxSalary.HasValue && j.MaxSalary <= salaryMax.Value));

        if (!string.IsNullOrWhiteSpace(experience))
            query = query.Where(j => j.ExperienceRequired == experience);

        if (!string.IsNullOrWhiteSpace(education))
            query = query.Where(j => j.EducationLevel == education);

        if (!string.IsNullOrWhiteSpace(workSchedule))
            query = query.Where(j => j.WorkSchedule == workSchedule);

        if (!string.IsNullOrWhiteSpace(languages))
            query = query.Where(j => j.Languages != null && j.Languages.Contains(languages));

        if (!string.IsNullOrWhiteSpace(benefits))
            query = query.Where(j => j.Benefits != null && j.Benefits.Contains(benefits));

        if (datePosted.HasValue && datePosted.Value > 0)
        {
            var since = DateTime.UtcNow.AddDays(-datePosted.Value);
            query = query.Where(j => j.CreatedAt >= since);
        }

        // ── Ce que le candidat a dit ne pas vouloir voir ──
        //
        // Sans ce filtre, ecarter une offre ne l'ecarterait de rien : elle
        // reviendrait a la visite suivante, et le bouton serait un
        // mensonge. On ne l'applique qu'a un candidat identifie — un
        // visiteur anonyme n'a rien declare, et un recruteur qui parcourt
        // le catalogue n'a pas a voir le sien ampute.
        var moi = GetUserId();
        if (moi != null && User.IsInRole("Candidate"))
        {
            var ecartees = await _context.OffresEcartees.AsNoTracking()
                .Where(o => o.UserId == moi)
                .Select(o => o.JobOfferId)
                .ToListAsync();
            if (ecartees.Count > 0)
                query = query.Where(j => !ecartees.Contains(j.Id));

            var prefs = await _context.PreferencesEmploi.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == moi);
            var exclus = prefs?.Exclus() ?? Array.Empty<string>();

            // Le rapprochement se fait sur la categorie, seul champ
            // structure qui porte la famille de metier. Chercher dans le
            // titre ecarterait « developpeur commercial » de quelqu'un qui
            // ne veut pas de vente, ce qui n'est pas ce qu'il a demande.
            foreach (var famille in exclus)
            {
                var f = famille;
                query = query.Where(j => j.Category == null || j.Category != f);
            }
        }

        // ── Tri ──
        //
        // Le tri « pertinence » n'en etait pas un : il classait par « a la
        // une », puis « urgent », puis date, sans jamais regarder ce que
        // le candidat avait tape. Une offre intitulee exactement comme sa
        // recherche sortait derriere une offre a la une sans rapport.
        //
        // Un vrai classement par pertinence se calcule sur l'offre entiere
        // et ne s'exprime pas en SQL : il faut donc materialiser avant de
        // trier, comme le fait deja la recherche par rayon. On ne le fait
        // que lorsqu'il y a une recherche a satisfaire — parcourir le
        // catalogue sans mot-clef reste pagine par la base.
        var pertinence = (string.IsNullOrWhiteSpace(sort) || sort == "relevance")
                         && (requete.MotsClefs.Count > 0 || requete.Metier is not null);

        // Trier, puis rendre une page. Le socle des filtres est fige a ce
        // stade ; seul le plein texte reste a poser, et il peut l'etre de
        // deux facons — d'ou la fonction plutot que la suite d'instructions.
        async Task<(List<JobOffer> Page, int Total)> Servir(IQueryable<JobOffer> filtre)
        {
            filtre = pertinence
                // Les plus recentes d'abord : c'est ce lot borne qui sera
                // ensuite reclasse, autant qu'il contienne ce qui compte.
                ? filtre.OrderByDescending(j => j.CreatedAt)
                : sort switch
                {
                    "date" => filtre.OrderByDescending(j => j.CreatedAt),
                    "salary_asc" => filtre.OrderBy(j => j.MinSalary ?? 0),
                    "salary_desc" => filtre.OrderByDescending(j => j.MaxSalary ?? 0),
                    "views" => filtre.OrderByDescending(j => j.ViewCount),
                    _ => filtre.OrderByDescending(j => j.IsFeatured).ThenByDescending(j => j.IsUrgent).ThenByDescending(j => j.CreatedAt),
                };

            if (pertinence || (useRadius && center != null))
            {
                // Materialisation bornee, pour ne pas charger le catalogue.
                var candidates = await filtre.Take(3000).ToListAsync();

                if (useRadius && center != null)
                {
                    candidates = candidates
                        .Where(j => j.Latitude.HasValue && j.Longitude.HasValue
                            && lpdeBack.Services.GeoUtils.DistanceKm(center.Value.Lat, center.Value.Lng, j.Latitude.Value, j.Longitude.Value) <= rayonRetenu!.Value)
                        .ToList();
                }

                if (pertinence)
                {
                    candidates = candidates
                        .OrderByDescending(j => lpdeBack.Services.RequeteLibre.Pertinence(requete, j))
                        .ThenByDescending(j => j.CreatedAt)
                        .ToList();
                }

                return (candidates.Skip((page - 1) * pageSize).Take(pageSize).ToList(), candidates.Count);
            }

            // Pagination côté SQL — indispensable avec un gros volume d'offres.
            var total = await filtre.CountAsync();
            return (await filtre.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(), total);
        }

        // ── Le plein texte, en deux temps ──
        //
        // Chercher dans les descriptions coute dix fois le reste : elles
        // font mille quatre cents caracteres de moyenne et vivent hors
        // page, quand le titre, la societe et les etiquettes tiennent dans
        // un index. Sur le catalogue reel, « developpeur » demandait
        // 4 554 ms avec les descriptions et 165 ms sans.
        //
        // On les ouvre donc en second recours, quand les colonnes courtes
        // ne rendent pas de quoi remplir une page. Une recherche rare —
        // celle qui justifie qu'on aille fouiller les descriptions — rend
        // exactement les memes offres qu'avant. Une recherche courante n'y
        // va plus : ce qu'elle y perdait etait de toute facon classe en
        // queue de liste par la pertinence, qui note un titre vingt-cinq
        // et une description un.
        //
        // Le seuil est une constante et non « page x pageSize », comme il
        // l'etait d'abord : sinon le nombre total d'offres annonce pour
        // une meme recherche changeait selon la taille de page demandee.
        // « kubernetes » rendait une offre en pageSize=1 et soixante-treize
        // en pageSize=24.
        IQueryable<JobOffer> AvecLesMots(bool descriptions) => descriptions
            ? query.Where(j => termes.Any(m => j.Title.Contains(m) || j.Company.Contains(m)
                  || (j.Tags != null && j.Tags.Contains(m)) || j.Description.Contains(m)))
            : query.Where(j => termes.Any(m => j.Title.Contains(m) || j.Company.Contains(m)
                  || (j.Tags != null && j.Tags.Contains(m))));

        List<JobOffer> results;
        int total;

        if (termes.Count == 0)
        {
            (results, total) = await Servir(query);
        }
        else
        {
            (results, total) = await Servir(AvecLesMots(false));

            if (total < SeuilDescriptions)
                (results, total) = await Servir(AvecLesMots(true));
        }

        Response.Headers["X-Total-Count"] = total.ToString();

        return results;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<JobOffer>> GetById(int id)
    {
        var userId = GetUserId();
        var job = await _context.JobOffers.Include(j => j.Applications).FirstOrDefaultAsync(j => j.Id == id);
        if (job == null) return NotFound();

        // Un brouillon n'existe que pour son auteur (et l'administration) :
        // sans ce garde-fou, l'adresse directe le rendrait public.
        if (job.IsDraft && !IsAdmin() && !await _perimetre.PeutGerer(userId, job.CreatedByUserId)) return NotFound();

        // Increment view count
        job.ViewCount++;
        await _context.SaveChangesAsync();

        // Only show applications if the logged-in user is the creator of this offer or an admin
        var isOwner = userId != null && await _perimetre.PeutGerer(userId, job.CreatedByUserId);
        if (!isOwner && !IsAdmin())
        {
            job.Applications = new List<Application>();
        }

        return job;
    }

    /// <summary>
    /// Ce que le site a compris d'une recherche, avant de la lancer.
    ///
    /// La liste d'offres reste un tableau : y glisser un objet
    /// d'explication aurait casse toutes les pages deja servies aux
    /// visiteurs le temps d'un deploiement. L'interface interroge donc ce
    /// point d'entree en parallele, affiche les etiquettes — « alternance »,
    /// « a moins de 25 km de Perpignan » — et laisse le candidat en
    /// retirer une. Un filtre applique sans etre montre est un filtre
    /// qu'on ne peut pas contester.
    ///
    /// C'est aussi le seul endroit ou un modele de langage intervient dans
    /// la recherche, et sous trois conditions : que les regles aient
    /// laisse une phrase entiere de cote, que le visiteur soit connecte —
    /// sinon un robot d'indexation epuiserait le quota du jour pour tout
    /// le monde — et que le quota ne soit pas deja atteint. Les trois
    /// echouent en silence : on rend ce que les regles ont trouve.
    /// </summary>
    [HttpGet("comprendre")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> Comprendre([FromQuery] string? q, CancellationToken ct)
    {
        var requete = RequeteLibre.Analyser(q);
        var assiste = false;

        if (GetUserId() is not null && requete.MeriteUneRelecture && _assistant.Disponible)
        {
            var avant = requete.Compris.Count;
            requete = await _assistant.Relire(q!, requete, ct);
            assiste = requete.Compris.Count > avant;
        }

        return Ok(new
        {
            compris = requete.Compris,
            metier = requete.Metier,
            contrat = requete.Contrat,
            lieu = requete.Lieu,
            rayonKm = requete.RayonKm,
            distanciel = requete.Distanciel,
            salaireAnnuelMinimum = requete.SalaireAnnuelMinimum,
            motsClefs = requete.MotsClefs,
            // L'interface doit pouvoir le dire : une phrase relue par un
            // modele n'a pas le meme statut qu'un filtre tire d'une regle.
            assiste,
        });
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<string>>> GetCategories()
    {
        return await _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved").Select(j => j.Category).Distinct().OrderBy(c => c).ToListAsync();
    }

    [HttpGet("stats")]
    public async Task<ActionResult<object>> GetStats()
    {
        var totalOffers = await _context.JobOffers.CountAsync(j => j.IsActive && j.ModerationStatus == "Approved");
        var totalApplications = await _context.Applications.CountAsync();
        var totalCompanies = await _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved").Select(j => j.Company).Distinct().CountAsync();
        var remoteOffers = await _context.JobOffers.CountAsync(j => j.IsActive && j.ModerationStatus == "Approved" && j.IsRemote);

        return new { totalOffers, totalApplications, totalCompanies, remoteOffers };
    }

    [HttpGet("moderation-required")]
    public async Task<ActionResult<object>> IsModerationRequired()
    {
        var val = await _context.PlatformSettings
            .Where(s => s.Key == "require_moderation")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        return new { required = val == "true" };
    }

    /// <summary>Une entree de la page « Parcourir » : un libelle et son nombre d'offres.</summary>
    public record BrowseFacet(string label, int count);

    /// <summary>Un libelle et la forme sur laquelle on le cherche.</summary>
    private record BrowseEntry(BrowseFacet facet, string key);

    private const int BrowsePreview = 24;      // ce qu'on sert sans que l'utilisateur ait demande plus
    private const int BrowseMaxLocations = 300;

    /// <summary>
    /// Forme de comparaison d'un libelle : sans accent ni majuscule. « Developpeur »
    /// doit trouver « Developpeur / Developpeuse web », personne ne tape les accents
    /// dans un champ de filtre.
    /// </summary>
    private static string BrowseKey(string label)
    {
        var decomposed = label.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>
    /// Agregat d'une section, en cache dix minutes. Chacun de ces GROUP BY balaie la
    /// table entiere des offres : on le paie une fois, puis la pagination et la
    /// recherche se font en memoire sans retoucher la base.
    /// </summary>
    private async Task<List<BrowseEntry>> GetBrowseFacetsAsync(string section)
    {
        var cached = await _cache.GetOrCreateAsync(lpdeBack.Services.BrowseCache.Key(section), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);

            var active = _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved");
            var grouped = section switch
            {
                "categories" => active.Where(j => j.Category != "").GroupBy(j => j.Category),
                "locations" => active.Where(j => j.Location != "").GroupBy(j => j.Location),
                _ => active.Where(j => j.ContractType != "").GroupBy(j => j.ContractType),
            };

            var ordered = grouped
                .Select(g => new { label = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ThenBy(x => x.label);

            // Les lieux sont bruites (une ligne par commune) : au-dela de 300 entrees
            // c'est du remplissage, personne ne parcourt jusque-la.
            var rows = section == "locations"
                ? await ordered.Take(BrowseMaxLocations).ToListAsync()
                : await ordered.ToListAsync();

            return rows
                .Select(r => new BrowseEntry(new BrowseFacet(r.label, r.count), BrowseKey(r.label)))
                .ToList();
        });

        return cached ?? new List<BrowseEntry>();
    }

    /// <summary>Apercu des trois sections de la page « Parcourir », avec les totaux.</summary>
    [HttpGet("browse")]
    public async Task<ActionResult<object>> Browse()
    {
        var categories = await GetBrowseFacetsAsync("categories");
        var locations = await GetBrowseFacetsAsync("locations");
        var contractTypes = await GetBrowseFacetsAsync("contractTypes");

        // Volontairement tronque : la liste complete des metiers depasse le millier
        // d'entrees et n'a aucune raison de traverser le reseau d'un seul bloc.
        return new
        {
            categories = categories.Take(BrowsePreview).Select(e => e.facet),
            categoriesTotal = categories.Count,
            locations = locations.Take(BrowsePreview).Select(e => e.facet),
            locationsTotal = locations.Count,
            contractTypes = contractTypes.Take(BrowsePreview).Select(e => e.facet),
            contractTypesTotal = contractTypes.Count,
        };
    }

    /// <summary>Une seule section de la page « Parcourir », paginee et filtrable.</summary>
    [HttpGet("browse/{section}")]
    public async Task<ActionResult<object>> BrowseSection(
        string section,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = BrowsePreview)
    {
        if (section is not ("categories" or "locations" or "contractTypes"))
            return BadRequest(new { message = "Section de parcours inconnue." });

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IEnumerable<BrowseEntry> entries = await GetBrowseFacetsAsync(section);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = BrowseKey(search.Trim());
            entries = entries.Where(e => e.key.Contains(needle, StringComparison.Ordinal));
        }

        var list = entries.ToList();
        return new
        {
            items = list.Skip((page - 1) * pageSize).Take(pageSize).Select(e => e.facet),
            total = list.Count,
            page,
            pageSize,
        };
    }

    // Valeurs réellement présentes en base pour les filtres avancés,
    // afin de n'afficher aucune option qui renverrait 0 résultat.
    [HttpGet("filters")]
    public async Task<ActionResult<object>> Filters()
    {
        var active = _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved");

        var experiences = await active
            .Where(j => j.ExperienceRequired != null && j.ExperienceRequired != "")
            .Select(j => j.ExperienceRequired!).Distinct().ToListAsync();
        var educations = await active
            .Where(j => j.EducationLevel != null && j.EducationLevel != "")
            .Select(j => j.EducationLevel!).Distinct().ToListAsync();
        var workSchedules = await active
            .Where(j => j.WorkSchedule != null && j.WorkSchedule != "")
            .Select(j => j.WorkSchedule!).Distinct().ToListAsync();

        // Les langues peuvent être stockées en liste "Anglais, Espagnol" → on éclate.
        var rawLangs = await active
            .Where(j => j.Languages != null && j.Languages != "")
            .Select(j => j.Languages!).ToListAsync();
        var languages = rawLangs
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x).ToList();

        return new
        {
            experiences = experiences.OrderBy(x => x).ToList(),
            educations = educations.OrderBy(x => x).ToList(),
            workSchedules = workSchedules.OrderBy(x => x).ToList(),
            languages,
        };
    }

    [HttpGet("companies")]
    public async Task<ActionResult<IEnumerable<object>>> GetCompanies(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 24)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 24 : pageSize;

        // Les libelles generiques — « Entreprise », « MAIRIE », « CCAS »… — ne
        // designent pas un employeur mais une categorie. Les laisser ici les
        // hisserait en tete du classement, chacun regroupant des milliers d'offres
        // n'ayant que leur libelle en commun.
        //
        // Comparaison sur ToUpper() plutot que sur la collation de la base : le
        // resultat ne doit pas dependre de la configuration du serveur.
        var active = _context.JobOffers.Where(j =>
            j.IsActive && j.ModerationStatus == "Approved"
            && !lpdeBack.Services.CompanyNames.Generic.Contains(j.Company.ToUpper()));

        if (!string.IsNullOrWhiteSpace(search))
            active = active.Where(j => j.Company.Contains(search) || j.Location.Contains(search));

        // Agrégats scalaires uniquement (COUNT / COUNT DISTINCT / MIN) : une seule
        // requête GROUP BY, sans matérialiser la liste des lieux par entreprise.
        var grouped = active
            .GroupBy(j => j.Company)
            .Select(g => new
            {
                company = g.Key,
                jobCount = g.Count(),
                siteCount = g.Select(j => j.Location).Distinct().Count(),
                location = g.Min(j => j.Location),
            });

        var total = await grouped.CountAsync();
        Response.Headers["X-Total-Count"] = total.ToString();

        var companies = await grouped
            .OrderByDescending(c => c.jobCount).ThenBy(c => c.company)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return Ok(companies);
    }

    [HttpGet("company/{companyName}")]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetByCompany(string companyName,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        // Pas de fiche pour un libelle generique : la page n'aurait aucun sens,
        // elle empilerait des offres n'ayant que ce libelle en commun.
        if (lpdeBack.Services.CompanyNames.IsGeneric(companyName))
            return NotFound(new { message = "Ce libelle ne designe pas un employeur en particulier." });

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 50 : pageSize;

        var q = _context.JobOffers
            .Where(j => j.IsActive && j.ModerationStatus == "Approved" && j.Company == companyName);
        Response.Headers["X-Total-Count"] = (await q.CountAsync()).ToString();
        return await q
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
    }

    /// <summary>Autocompletion : suggestions de mots-cles (titres/entreprises) ou de lieux.</summary>
    [HttpGet("suggest")]
    public async Task<ActionResult<IEnumerable<string>>> Suggest([FromQuery] string? q, [FromQuery] string type = "keyword")
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(Array.Empty<string>());

        var active = _context.JobOffers.Where(j => j.IsActive && j.ModerationStatus == "Approved");

        if (type == "location")
        {
            var locations = await active
                .Where(j => j.Location.Contains(q))
                .Select(j => j.Location)
                .Distinct()
                .OrderBy(l => l)
                .Take(8)
                .ToListAsync();
            return Ok(locations);
        }

        // keyword : titres + entreprises
        var titles = await active.Where(j => j.Title.Contains(q)).Select(j => j.Title).Distinct().Take(6).ToListAsync();
        var companies = await active
            .Where(j => j.Company.Contains(q)
                     && !lpdeBack.Services.CompanyNames.Generic.Contains(j.Company.ToUpper()))
            .Select(j => j.Company).Distinct().Take(4).ToListAsync();
        var suggestions = titles.Concat(companies).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        return Ok(suggestions);
    }

    /// <summary>Signaler une offre d'emploi (accessible sans authentification).</summary>
    [HttpPost("{id}/report")]
    [AllowAnonymous]
    public async Task<IActionResult> Report(int id, JobReportDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new { message = "Un motif est requis." });

        var exists = await _context.JobOffers.AnyAsync(j => j.Id == id);
        if (!exists) return NotFound();

        var report = new JobReport
        {
            JobOfferId = id,
            Reason = dto.Reason,
            Details = dto.Details,
            ReporterEmail = dto.ReporterEmail,
            ReporterUserId = GetUserId(),
        };
        _context.JobReports.Add(report);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Signalement enregistre. Merci." });
    }

    /// <summary>Admin : liste des signalements d'offres.</summary>
    [HttpGet("reports")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<object>>> GetReports([FromQuery] string? status)
    {
        var query = _context.JobReports.Include(r => r.JobOffer).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        return Ok(await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id, r.JobOfferId, r.Reason, r.Details, r.ReporterEmail, r.Status, r.CreatedAt,
                jobTitle = r.JobOffer != null ? r.JobOffer.Title : null,
                company = r.JobOffer != null ? r.JobOffer.Company : null,
            })
            .ToListAsync());
    }

    /// <summary>Admin : mettre a jour le statut d'un signalement.</summary>
    [HttpPatch("reports/{reportId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateReport(int reportId, [FromBody] JobReportDto dto)
    {
        var report = await _context.JobReports.FindAsync(reportId);
        if (report == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(dto.Reason)) report.Status = dto.Reason; // reutilise Reason comme nouveau statut
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ── Protected endpoints (Recruiter / Admin) ──

    [HttpGet("mine")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<IEnumerable<JobOffer>>> GetMyOffers([FromQuery] string? scope)
    {
        var userId = GetUserId();
        IQueryable<JobOffer> query;

        if (IsAdmin())
        {
            query = _context.JobOffers.AsQueryable();
        }
        else if (scope == "team")
        {
            var me = await _userManager.FindByIdAsync(userId!);
            if (!string.IsNullOrWhiteSpace(me?.Company))
            {
                var teamIds = await _userManager.Users.Where(u => u.Company == me.Company).Select(u => u.Id).ToListAsync();
                query = _context.JobOffers.Where(j => j.CreatedByUserId != null && teamIds.Contains(j.CreatedByUserId));
            }
            else
            {
                query = _context.JobOffers.Where(j => j.CreatedByUserId == userId);
            }
        }
        else
        {
            var equipe = await _perimetre.Equipe(userId);
            query = _context.JobOffers.Where(j => j.CreatedByUserId != null && equipe.Contains(j.CreatedByUserId));
        }

        return await query
            .Include(j => j.Applications)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    /// <summary>Coéquipiers de recrutement (recruteurs de la même entreprise) + nombre d'offres.</summary>
    [HttpGet("team-members")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<object>> GetTeamMembers()
    {
        var userId = GetUserId();
        var me = await _userManager.FindByIdAsync(userId!);
        if (string.IsNullOrWhiteSpace(me?.Company)) return new { company = (string?)null, members = new object[0] };

        var teammates = await _userManager.Users
            .Where(u => u.Company == me.Company && (u.Role == "Recruiter" || u.Role == "Admin"))
            .ToListAsync();
        var ids = teammates.Select(u => u.Id).ToList();
        var counts = await _context.JobOffers
            .Where(j => j.CreatedByUserId != null && ids.Contains(j.CreatedByUserId))
            .GroupBy(j => j.CreatedByUserId!)
            .Select(g => new { userId = g.Key, count = g.Count() })
            .ToListAsync();

        var members = teammates.Select(u => new
        {
            name = $"{u.FirstName} {u.LastName}",
            role = u.Role,
            isMe = u.Id == userId,
            offerCount = counts.FirstOrDefault(c => c.userId == u.Id)?.count ?? 0,
        }).OrderByDescending(m => m.offerCount).ToList();

        return new { company = me.Company, members };
    }

    public sealed class EtatOffreDto
    {
        [EtatOffre]
        public string Etat { get; set; } = string.Empty;
    }

    /// <summary>
    /// Ouvrir, suspendre ou fermer une offre.
    ///
    /// Le seul geste disponible etait la suppression, qui emporte les
    /// candidatures deja recues — un prix qu'aucun recruteur ne devrait
    /// payer pour mettre une annonce en pause le temps d'un arbitrage.
    ///
    /// Une offre fermee ou suspendue sort du catalogue de la meme facon :
    /// la difference n'existe que pour celui qui la gere, et c'est
    /// exactement ce qu'on veut. Une offre pourvue et une offre en
    /// attente d'arbitrage ne se ressemblaient plus que par accident.
    /// </summary>
    [HttpPatch("{id}/etat")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<object>> ChangerEtat(int id, EtatOffreDto dto)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), job.CreatedByUserId)) return Forbid();

        if (!EtatOffre.Existe(dto.Etat)) return BadRequest(new { message = "État inconnu." });

        // Un brouillon n'a pas d'etat de publication : il n'a jamais ete
        // publie. Le suspendre n'aurait pas de sens, et le rouvrir
        // sauterait la moderation.
        if (job.IsDraft)
            return BadRequest(new { message = "Ce brouillon n'a jamais été publié : terminez sa rédaction pour le mettre en ligne." });

        // Rouvrir une offre que la moderation a refusee la remettrait en
        // ligne sans second regard.
        if (dto.Etat == EtatOffre.Ouverte && job.ModerationStatus != "Approved")
            return BadRequest(new { message = "Cette offre attend la modération : elle ne peut pas être rouverte d'ici." });

        EtatOffre.Appliquer(job, dto.Etat);
        await _context.SaveChangesAsync();

        return Ok(new { job.Id, job.EtatPublication, job.IsActive });
    }

    [HttpPatch("{id}/renew")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<JobOffer>> RenewOffer(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), job.CreatedByUserId)) return Forbid();
        if (job.IsDraft) return BadRequest(new { message = "Ce brouillon n'a jamais ete publie : terminez sa redaction pour le mettre en ligne." });

        var durationStr = await _context.PlatformSettings
            .Where(s => s.Key == "default_offer_duration")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        var duration = int.TryParse(durationStr, out var d) ? d : 30;

        EtatOffre.Appliquer(job, true);
        job.ExpiresAt = DateTime.UtcNow.AddDays(duration);
        job.ModerationStatus = "Approved"; // Renewal re-approves
        await _context.SaveChangesAsync();
        return Ok(job);
    }

    /// <summary>Recruteur/Admin : sponsoriser (mettre en avant) sa propre offre.</summary>
    [HttpPatch("{id}/feature")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<object>> ToggleFeatureOwn(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), job.CreatedByUserId)) return Forbid();
        job.IsFeatured = !job.IsFeatured;
        await _context.SaveChangesAsync();
        return new { isFeatured = job.IsFeatured };
    }

    /// <summary>Recruteur/Admin : statistiques d'une offre (vues, candidatures, conversion, statuts).</summary>
    [HttpGet("{id}/stats")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<object>> GetOfferStats(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), job.CreatedByUserId)) return Forbid();

        var apps = await _context.Applications.Where(a => a.JobOfferId == id).ToListAsync();
        var byStatus = apps.GroupBy(a => a.Status).Select(g => new { label = g.Key, value = g.Count() }).ToList();
        var thirty = DateTime.UtcNow.AddDays(-30);
        var appsByDay = apps.Where(a => a.AppliedAt >= thirty)
            .GroupBy(a => a.AppliedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .OrderBy(x => x.label).ToList();
        var conversion = job.ViewCount > 0 ? Math.Round((double)apps.Count / job.ViewCount * 100, 1) : 0;

        return new { views = job.ViewCount, applications = apps.Count, isFeatured = job.IsFeatured, conversion, byStatus, appsByDay };
    }

    [HttpGet("stats/detailed")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<ActionResult<object>> GetDetailedStats()
    {
        var offersByCategory = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Category)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToListAsync();

        var offersByContract = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.ContractType)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToListAsync();

        var appsByStatus = await _context.Applications
            .GroupBy(a => a.Status)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToListAsync();

        var topCompanies = await _context.JobOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Company)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .Take(5)
            .ToListAsync();

        var recentApps = await _context.Applications
            .OrderByDescending(a => a.AppliedAt)
            .Take(30)
            .GroupBy(a => a.AppliedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .ToListAsync();

        return new { offersByCategory, offersByContract, appsByStatus, topCompanies, recentApps };
    }

    /// <summary>Admin: statistiques complètes de la plateforme</summary>
    // ═══════════════════════════════════════════════════════════
    //  STATISTIQUES — par section
    //
    //  La version d'origine chargeait toutes les offres en memoire pour
    //  les agreger en LINQ. A deux cent quarante mille lignes, description
    //  comprise, la reponse passait cinq secondes et pesait deux cents
    //  kilo-octets — et grossissait a chaque import.
    //
    //  Les comptages se font desormais en base, et la page ne demande que
    //  la section regardee. On ne transporte plus les lignes, seulement
    //  leurs totaux.
    // ═══════════════════════════════════════════════════════════

    /// <summary>Compte les jours d'une serie sur trente jours, formate cote serveur.</summary>
    private static List<object> ParJour(List<(DateTime Jour, int Nombre)> brut, DateTime depuis)
    {
        // On complete les jours sans donnee : une courbe trouee se lit mal,
        // et l'absence d'activite est une information.
        var index = brut.ToDictionary(x => x.Jour.Date, x => x.Nombre);
        return Enumerable.Range(0, 30)
            .Select(i => depuis.AddDays(i).Date)
            .Select(j => (object)new { label = j.ToString("dd/MM"), value = index.GetValueOrDefault(j, 0) })
            .ToList();
    }

    [HttpGet("stats/admin/apercu")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> GetAdminApercu()
    {
        var now = DateTime.UtcNow;
        var j30 = now.AddDays(-30);
        var j7 = now.AddDays(-7);

        var offres = _context.JobOffers;
        var users = _userManager.Users;
        var apps = _context.Applications;

        return Ok(new
        {
            totalUsers = await users.CountAsync(),
            totalCandidates = await users.CountAsync(u => u.Role == "Candidate"),
            totalRecruiters = await users.CountAsync(u => u.Role == "Recruiter"),
            totalAdmins = await users.CountAsync(u => u.Role == "Admin"),
            usersLast30d = await users.CountAsync(u => u.CreatedAt >= j30),
            usersLast7d = await users.CountAsync(u => u.CreatedAt >= j7),
            onlineNow = lpdeBack.Hubs.ChatHub.GetOnlineUserIds().Count(),

            totalOffers = await offres.CountAsync(),
            activeOffers = await offres.CountAsync(j => j.IsActive),
            expiredOffers = await offres.CountAsync(j => !j.IsActive),
            urgentOffers = await offres.CountAsync(j => j.IsUrgent && j.IsActive),
            remoteOffers = await offres.CountAsync(j => j.IsRemote && j.IsActive),
            offersLast30d = await offres.CountAsync(j => j.CreatedAt >= j30),
            // SUM cote base : additionner en memoire supposerait de
            // rapatrier les deux cent quarante mille compteurs de vues.
            totalViews = await offres.SumAsync(j => (long)j.ViewCount),

            totalApplications = await apps.CountAsync(),
            appsLast30d = await apps.CountAsync(a => a.AppliedAt >= j30),
            appsLast7d = await apps.CountAsync(a => a.AppliedAt >= j7),

            totalInterviews = await _context.Interviews.CountAsync(),
            totalMessages = await _context.Messages.CountAsync(),
            messagesLast30d = await _context.Messages.CountAsync(m => m.CreatedAt >= j30),
        });
    }

    [HttpGet("stats/admin/offres")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> GetAdminStatsOffres()
    {
        var j30 = DateTime.UtcNow.AddDays(-30);
        var actives = _context.JobOffers.Where(j => j.IsActive);

        var jours = await _context.JobOffers
            .Where(j => j.CreatedAt >= j30)
            .GroupBy(j => j.CreatedAt.Date)
            .Select(g => new { Jour = g.Key, Nombre = g.Count() })
            .ToListAsync();

        return Ok(new
        {
            offersByDay = ParJour(jours.Select(x => (x.Jour, x.Nombre)).ToList(), j30),

            // Les offres importees portent une categorie en texte libre :
            // on en compte plus de quinze cents distinctes. Un graphique a
            // quinze cents barres ne se lit pas — on sert le sommet, et le
            // titre annonce que c'en est un.
            offersByCategory = await actives
                .GroupBy(j => j.Category)
                .Select(g => new { label = g.Key, value = g.Count() })
                .OrderByDescending(x => x.value).Take(12).ToListAsync(),

            offersByContract = await actives
                .GroupBy(j => j.ContractType)
                .Select(g => new { label = g.Key, value = g.Count() })
                .OrderByDescending(x => x.value).ToListAsync(),

            offersByExperience = await actives
                .Where(j => j.ExperienceRequired != null && j.ExperienceRequired != "")
                .GroupBy(j => j.ExperienceRequired!)
                .Select(g => new { label = g.Key, value = g.Count() })
                .OrderByDescending(x => x.value).ToListAsync(),

            // Le titre est tronque apres la selection : le decouper en SQL
            // obligerait a des fonctions propres a chaque moteur.
            topViewedOffers = (await actives
                .OrderByDescending(j => j.ViewCount)
                .Take(10)
                .Select(j => new { j.Title, j.ViewCount, j.Company })
                .ToListAsync())
                .Select(j => new
                {
                    label = j.Title.Length > 35 ? j.Title[..35] + "..." : j.Title,
                    value = j.ViewCount,
                    company = j.Company,
                }).ToList(),

            offersByLocation = await actives
                .Where(j => j.Location != null && j.Location != "")
                .GroupBy(j => j.Location)
                .Select(g => new { label = g.Key, value = g.Count() })
                .OrderByDescending(x => x.value).Take(20).ToListAsync(),

            salaryByCategory = await actives
                .Where(j => j.MinSalary != null && j.MaxSalary != null)
                .GroupBy(j => j.Category)
                .Select(g => new
                {
                    label = g.Key,
                    min = (int)g.Average(j => j.MinSalary!.Value),
                    max = (int)g.Average(j => j.MaxSalary!.Value),
                })
                .OrderByDescending(x => x.max).Take(12).ToListAsync(),

            topCompanies = await actives
                .GroupBy(j => j.Company)
                .Select(g => new { label = g.Key, value = g.Count() })
                .OrderByDescending(x => x.value).Take(10).ToListAsync(),
        });
    }

    [HttpGet("stats/admin/candidatures")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> GetAdminStatsCandidatures()
    {
        var j30 = DateTime.UtcNow.AddDays(-30);

        var jours = await _context.Applications
            .Where(a => a.AppliedAt >= j30)
            .GroupBy(a => a.AppliedAt.Date)
            .Select(g => new { Jour = g.Key, Nombre = g.Count() })
            .ToListAsync();

        // Taux de conversion : vues rapportees aux candidatures, par
        // entreprise. Les deux cotes s'agregent separement puis se
        // rejoignent en memoire — sur dix lignes, c'est sans consequence.
        var candParEntreprise = await _context.Applications
            .Where(a => a.JobOffer != null)
            .GroupBy(a => a.JobOffer!.Company)
            .Select(g => new { Entreprise = g.Key, Candidatures = g.Count() })
            .OrderByDescending(x => x.Candidatures).Take(10).ToListAsync();

        var noms = candParEntreprise.Select(x => x.Entreprise).ToList();
        var vuesParEntreprise = await _context.JobOffers
            .Where(j => noms.Contains(j.Company))
            .GroupBy(j => j.Company)
            .Select(g => new { Entreprise = g.Key, Vues = g.Sum(j => j.ViewCount) })
            .ToListAsync();

        var conversion = candParEntreprise.Select(c =>
        {
            var vues = vuesParEntreprise.FirstOrDefault(v => v.Entreprise == c.Entreprise)?.Vues ?? 0;
            return new
            {
                label = c.Entreprise,
                value = vues > 0 ? Math.Round(c.Candidatures * 100.0 / vues, 1) : 0,
            };
        }).OrderByDescending(x => x.value).ToList();

        return Ok(new
        {
            appsByStatus = await _context.Applications
                .GroupBy(a => a.Status)
                .Select(g => new { label = g.Key, value = g.Count() })
                .ToListAsync(),

            appsByDay = ParJour(jours.Select(x => (x.Jour, x.Nombre)).ToList(), j30),

            appsBySource = await _context.Applications
                .GroupBy(a => a.Source ?? "Directe")
                .Select(g => new { label = g.Key, value = g.Count() })
                .OrderByDescending(x => x.value).ToListAsync(),

            conversionByCompany = conversion,
        });
    }

    [HttpGet("stats/admin/utilisateurs")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> GetAdminStatsUtilisateurs()
    {
        var j30 = DateTime.UtcNow.AddDays(-30);
        var users = _userManager.Users;

        var jours = await users
            .Where(u => u.CreatedAt >= j30)
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { Jour = g.Key, Nombre = g.Count() })
            .ToListAsync();

        async Task<List<object>> ParVille(string? role) =>
            (await users
                .Where(u => u.City != null && u.City != "" && (role == null || u.Role == role))
                .GroupBy(u => u.City!)
                .Select(g => new { label = g.Key, value = g.Count() })
                .OrderByDescending(x => x.value).Take(15).ToListAsync())
            .Cast<object>().ToList();

        return Ok(new
        {
            registrationsByDay = ParJour(jours.Select(x => (x.Jour, x.Nombre)).ToList(), j30),
            usersByCity = await ParVille(null),
            candidatesByCity = await ParVille("Candidate"),
            recruitersByCity = await ParVille("Recruiter"),

            // La carte d'origine geographique superpose trois couches, dont
            // celle des offres. Elle vit dans cette section : la laisser
            // dans « Offres » rendait la couche vide tant qu'on n'avait pas
            // ouvert cet onglet-la.
            offersByLocation = await _context.JobOffers
                .Where(j => j.IsActive && j.Location != null && j.Location != "")
                .GroupBy(j => j.Location)
                .Select(g => new { label = g.Key, value = g.Count() })
                .OrderByDescending(x => x.value).Take(30).ToListAsync(),
        });
    }

    [HttpGet("stats/admin/echanges")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> GetAdminStatsEchanges()
    {
        var j30 = DateTime.UtcNow.AddDays(-30);

        var jours = await _context.Messages
            .Where(m => m.CreatedAt >= j30)
            .GroupBy(m => m.CreatedAt.Date)
            .Select(g => new { Jour = g.Key, Nombre = g.Count() })
            .ToListAsync();

        return Ok(new
        {
            interviewsByType = await _context.Interviews
                .Where(i => i.Type != null && i.Type != "")
                .GroupBy(i => i.Type!)
                .Select(g => new { label = g.Key, value = g.Count() })
                .OrderByDescending(x => x.value).ToListAsync(),

            interviewsByStatus = await _context.Interviews
                .GroupBy(i => i.Status)
                .Select(g => new { label = g.Key, value = g.Count() })
                .ToListAsync(),

            messagesByDay = ParJour(jours.Select(x => (x.Jour, x.Nombre)).ToList(), j30),
        });
    }

    [HttpGet("stats/admin/activite")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> GetAdminStatsActivite()
    {
        var j30 = DateTime.UtcNow.AddDays(-30);

        var offres = await _context.JobOffers.Where(j => j.CreatedAt >= j30)
            .GroupBy(j => j.CreatedAt.Date).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var cand = await _context.Applications.Where(a => a.AppliedAt >= j30)
            .GroupBy(a => a.AppliedAt.Date).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var inscr = await _userManager.Users.Where(u => u.CreatedAt >= j30)
            .GroupBy(u => u.CreatedAt.Date).Select(g => new { g.Key, N = g.Count() }).ToListAsync();

        var io = offres.ToDictionary(x => x.Key, x => x.N);
        var ic = cand.ToDictionary(x => x.Key, x => x.N);
        var iu = inscr.ToDictionary(x => x.Key, x => x.N);

        var timeline = Enumerable.Range(0, 30)
            .Select(i => j30.AddDays(i).Date)
            .Select(j => new
            {
                label = j.ToString("dd/MM"),
                offres = io.GetValueOrDefault(j, 0),
                candidatures = ic.GetValueOrDefault(j, 0),
                inscriptions = iu.GetValueOrDefault(j, 0),
            }).ToList();

        return Ok(new { activityTimeline = timeline });
    }

    /// <summary>
    /// Version historique, en un seul appel. Conservee pour tout appelant
    /// exterieur, mais la page d'administration ne l'utilise plus : elle
    /// charge desormais section par section.
    /// </summary>
    [HttpGet("stats/admin")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> GetAdminStats()
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var sevenDaysAgo = now.AddDays(-7);

        // ── Utilisateurs ──
        var allUsers = await _userManager.Users.ToListAsync();
        var totalUsers = allUsers.Count;
        var totalCandidates = allUsers.Count(u => u.Role == "Candidate");
        var totalRecruiters = allUsers.Count(u => u.Role == "Recruiter");
        var totalAdmins = allUsers.Count(u => u.Role == "Admin");
        var usersLast30d = allUsers.Count(u => u.CreatedAt >= thirtyDaysAgo);
        var usersLast7d = allUsers.Count(u => u.CreatedAt >= sevenDaysAgo);
        var onlineNow = ChatHub.GetOnlineUserIds().Count();

        // Inscriptions par jour (30 derniers jours)
        var registrationsByDay = allUsers
            .Where(u => u.CreatedAt >= thirtyDaysAgo)
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // Inscriptions par rôle par semaine (8 dernières semaines)
        var eightWeeksAgo = now.AddDays(-56);
        var registrationsByRoleWeek = allUsers
            .Where(u => u.CreatedAt >= eightWeeksAgo)
            .GroupBy(u => new { Week = System.Globalization.ISOWeek.GetWeekOfYear(u.CreatedAt), u.Role })
            .Select(g => new { week = g.Key.Week, role = g.Key.Role, count = g.Count() })
            .OrderBy(x => x.week)
            .ToList();

        // Répartition des villes des utilisateurs
        var usersByCity = allUsers
            .Where(u => !string.IsNullOrEmpty(u.City))
            .GroupBy(u => u.City!.Trim())
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .Take(20)
            .ToList();

        // Candidats par ville (pour la carte)
        var candidatesByCity = allUsers
            .Where(u => u.Role == "Candidate" && !string.IsNullOrEmpty(u.City))
            .GroupBy(u => u.City!.Trim())
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Recruteurs par ville (pour la carte)
        var recruitersByCity = allUsers
            .Where(u => (u.Role == "Recruiter" || u.Role == "Admin") && !string.IsNullOrEmpty(u.City))
            .GroupBy(u => u.City!.Trim())
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // ── Offres ──
        var allOffers = await _context.JobOffers.ToListAsync();
        var totalOffers = allOffers.Count;
        var activeOffers = allOffers.Count(j => j.IsActive);
        var expiredOffers = allOffers.Count(j => !j.IsActive);
        var urgentOffers = allOffers.Count(j => j.IsUrgent && j.IsActive);
        var remoteOffers = allOffers.Count(j => j.IsRemote && j.IsActive);
        var offersLast30d = allOffers.Count(j => j.CreatedAt >= thirtyDaysAgo);
        var totalViews = allOffers.Sum(j => j.ViewCount);

        // Offres publiées par jour (30 derniers jours)
        var offersByDay = allOffers
            .Where(j => j.CreatedAt >= thirtyDaysAgo)
            .GroupBy(j => j.CreatedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // Offres par catégorie
        var offersByCategory = allOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Category)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Offres par type de contrat
        var offersByContract = allOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.ContractType)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Offres par niveau d'expérience
        var offersByExperience = allOffers
            .Where(j => j.IsActive && !string.IsNullOrEmpty(j.ExperienceRequired))
            .GroupBy(j => j.ExperienceRequired!)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Top offres les plus vues
        var topViewedOffers = allOffers
            .Where(j => j.IsActive)
            .OrderByDescending(j => j.ViewCount)
            .Take(10)
            .Select(j => new { label = j.Title.Length > 35 ? j.Title.Substring(0, 35) + "..." : j.Title, value = j.ViewCount, company = j.Company })
            .ToList();

        // Géographie des offres (par ville)
        var offersByLocation = allOffers
            .Where(j => j.IsActive && !string.IsNullOrEmpty(j.Location))
            .GroupBy(j => j.Location.Trim())
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .Take(20)
            .ToList();

        // Salaires moyens par catégorie
        var salaryByCategory = allOffers
            .Where(j => j.IsActive && j.MinSalary.HasValue && j.MaxSalary.HasValue)
            .GroupBy(j => j.Category)
            .Select(g => new { label = g.Key, min = (int)g.Average(j => j.MinSalary!.Value), max = (int)g.Average(j => j.MaxSalary!.Value) })
            .OrderByDescending(x => x.max)
            .ToList();

        // Top entreprises
        var topCompanies = allOffers
            .Where(j => j.IsActive)
            .GroupBy(j => j.Company)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .Take(10)
            .ToList();

        // ── Candidatures ──
        var allApps = await _context.Applications.Include(a => a.JobOffer).ToListAsync();
        var totalApplications = allApps.Count;
        var appsLast30d = allApps.Count(a => a.AppliedAt >= thirtyDaysAgo);
        var appsLast7d = allApps.Count(a => a.AppliedAt >= sevenDaysAgo);

        // Candidatures par statut
        var appsByStatus = allApps
            .GroupBy(a => a.Status)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToList();

        // Candidatures par jour (30 derniers jours)
        var appsByDay = allApps
            .Where(a => a.AppliedAt >= thirtyDaysAgo)
            .GroupBy(a => a.AppliedAt.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // Candidatures par source
        var appsBySource = allApps
            .Where(a => !string.IsNullOrEmpty(a.Source))
            .GroupBy(a => a.Source!)
            .Select(g => new { label = g.Key, value = g.Count() })
            .OrderByDescending(x => x.value)
            .ToList();

        // Taux de conversion par entreprise (candidatures / vues)
        var conversionByCompany = allOffers
            .Where(j => j.IsActive && j.ViewCount > 0)
            .GroupBy(j => j.Company)
            .Select(g => new {
                label = g.Key,
                views = g.Sum(j => j.ViewCount),
                apps = allApps.Count(a => g.Select(j => j.Id).Contains(a.JobOfferId)),
            })
            .Where(x => x.apps > 0)
            .Select(x => new { x.label, value = Math.Round((double)x.apps / x.views * 100, 1) })
            .OrderByDescending(x => x.value)
            .Take(10)
            .ToList();

        // ── Entretiens ──
        var totalInterviews = await _context.Interviews.CountAsync();
        var interviewsByType = await _context.Interviews
            .Where(i => !string.IsNullOrEmpty(i.Type))
            .GroupBy(i => i.Type!)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToListAsync();

        var interviewsByStatus = await _context.Interviews
            .GroupBy(i => i.Status)
            .Select(g => new { label = g.Key, value = g.Count() })
            .ToListAsync();

        // ── Messagerie ──
        var totalMessages = await _context.Messages.CountAsync();
        var messagesLast30d = await _context.Messages.CountAsync(m => m.CreatedAt >= thirtyDaysAgo);
        var recentMessages = await _context.Messages
            .Where(m => m.CreatedAt >= thirtyDaysAgo)
            .Select(m => m.CreatedAt)
            .ToListAsync();
        var messagesByDay = recentMessages
            .GroupBy(d => d.Date)
            .Select(g => new { label = g.Key.ToString("dd/MM"), value = g.Count() })
            .OrderBy(x => x.label)
            .ToList();

        // ── Activité globale (timeline combinée) ──
        // Comptage par jour : offres + candidatures + inscriptions
        var activityDays = Enumerable.Range(0, 30).Select(i => thirtyDaysAgo.AddDays(i).Date).ToList();
        var activityTimeline = activityDays.Select(day => new {
            label = day.ToString("dd/MM"),
            offres = allOffers.Count(j => j.CreatedAt.Date == day),
            candidatures = allApps.Count(a => a.AppliedAt.Date == day),
            inscriptions = allUsers.Count(u => u.CreatedAt.Date == day),
        }).ToList();

        return Ok(new {
            // KPI principaux
            totalUsers, totalCandidates, totalRecruiters, totalAdmins,
            usersLast30d, usersLast7d, onlineNow,
            totalOffers, activeOffers, expiredOffers, urgentOffers, remoteOffers, offersLast30d, totalViews,
            totalApplications, appsLast30d, appsLast7d,
            totalInterviews, totalMessages, messagesLast30d,

            // Charts
            registrationsByDay, registrationsByRoleWeek,
            usersByCity, candidatesByCity, recruitersByCity,
            offersByDay, offersByCategory, offersByContract, offersByExperience,
            topViewedOffers, offersByLocation, salaryByCategory, topCompanies,
            appsByStatus, appsByDay, appsBySource, conversionByCompany,
            interviewsByType, interviewsByStatus,
            messagesByDay, activityTimeline,
        });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Recruiter")]
    // Une offre paraît en public : elle engage la plateforme autant que
    // son auteur. On veut savoir que l'adresse est bien la sienne.
    [AdresseConfirmee]
    public async Task<ActionResult<JobOffer>> Create(JobOfferCreateDto dto)
    {
        // ── Quota de la formule ──
        //
        // Le brouillon en est exempte : il n'est visible de personne, et
        // facturer un brouillon reviendrait a faire payer l'hesitation.
        // L'administration l'est aussi — elle depose pour autrui.
        //
        // Le meme controle existe sur l'API publique : sans lui, elle
        // serait la porte de service qui contourne la facturation.
        if (!dto.IsDraft && !IsAdmin())
        {
            var auteur = GetUserId();
            if (auteur is not null)
            {
                var (autorise, motif, _, _) = await _facturation.PeutPublier(auteur);
                // 402 « paiement requis » : ce n'est ni une erreur de
                // saisie ni un droit manquant, et le client sait alors
                // qu'il faut proposer la page de facturation.
                if (!autorise) return StatusCode(402, new { message = motif });
            }
        }

        // Check if moderation is required
        var requireModeration = await _context.PlatformSettings
            .Where(s => s.Key == "require_moderation")
            .Select(s => s.Value)
            .FirstOrDefaultAsync() == "true";

        // Admins bypass moderation
        var isAdmin = IsAdmin();
        // Un brouillon n'est pas soumis a moderation : il ne sera relu qu'au moment
        // ou le recruteur decidera de le publier.
        var needsReview = requireModeration && !isAdmin && !dto.IsDraft;

        // Get default offer duration from settings
        var durationStr = await _context.PlatformSettings
            .Where(s => s.Key == "default_offer_duration")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        var duration = int.TryParse(durationStr, out var d) ? d : 30;

        var job = new JobOffer
        {
            Title = dto.Title,
            Company = dto.Company,
            Location = dto.Location,
            Description = dto.Description,
            ContractType = dto.ContractType,
            Salary = dto.Salary,
            Category = dto.Category,
            IsRemote = dto.IsRemote,
            ExpiresAt = dto.ExpiresAt ?? DateTime.UtcNow.AddDays(duration),
            CompanyLogoUrl = dto.CompanyLogoUrl,
            Tags = dto.Tags,
            MinSalary = dto.MinSalary,
            MaxSalary = dto.MaxSalary,
            ExperienceRequired = dto.ExperienceRequired,
            EducationLevel = dto.EducationLevel,
            Benefits = dto.Benefits,
            WorkSchedule = dto.WorkSchedule,
            Languages = dto.Languages,
            CompanyDescription = dto.CompanyDescription,
            IsUrgent = dto.IsUrgent,
            EasyApply = dto.EasyApply,
            ScreeningQuestions = dto.ScreeningQuestions,
            AutoReplyMessage = dto.AutoReplyMessage,
            Openings = dto.Openings < 1 ? 1 : dto.Openings,
            WorkplaceType = dto.WorkplaceType,
            Address = dto.Address,
            SalaryPeriod = dto.SalaryPeriod,
            SupplementalPay = dto.SupplementalPay,
            ContractDuration = dto.ContractDuration,
            HoursPerWeek = dto.HoursPerWeek,
            StartDate = dto.StartDate,
            ApplicationEmail = dto.ApplicationEmail,
            RequireResume = dto.RequireResume,
            IsDraft = dto.IsDraft,
            CreatedByUserId = GetUserId(),
            ModerationStatus = needsReview ? "Pending" : "Approved",
            IsActive = !needsReview && !dto.IsDraft,
            EtatPublication = !needsReview && !dto.IsDraft ? EtatOffre.Ouverte : EtatOffre.Fermee,
        };

        // Le type de lieu de travail est la source de verite du drapeau teletravail,
        // sur lequel repose le filtre de recherche.
        if (!string.IsNullOrWhiteSpace(job.WorkplaceType))
            job.IsRemote = job.WorkplaceType is "Télétravail" or "Hybride";

        // Geocodage du lieu pour la recherche par rayon
        var geo = lpdeBack.Services.GeoUtils.Geocode(job.Location);
        if (geo != null) { job.Latitude = geo.Value.Lat; job.Longitude = geo.Value.Lng; }

        // L'empreinte de contenu : elle sert au dedoublonnage
        // inter-sources, et une offre deposee ici doit y participer —
        // sans quoi un import ramenerait la meme annonce a cote.
        job.Empreinte = lpdeBack.Services.QualiteCatalogue.Empreinte(job.Title, job.Company, job.Location);

        // La meme analyse que pour les offres importees. Une annonce
        // deposee sur le site n'est pas plus fiable qu'une autre — les
        // arnaques a l'emploi passent precisement par la ou l'on
        // controle le moins. Elle ne bloque rien : au-dela du seuil,
        // l'offre entre en file de moderation.
        if (!isAdmin && !dto.IsDraft && _qualite.Filtrer(job))
            needsReview = true;

        if (needsReview) job.ModerationStatus = "Pending";

        _context.JobOffers.Add(job);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Recruiter")]
    public async Task<IActionResult> Update(int id, JobOfferUpdateDto dto)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        // Le role suffisait a autoriser la modification : tout compte
        // recruteur pouvait donc reecrire l'offre de n'importe quel autre
        // — titre, description, remuneration, jusqu'au nom de
        // l'entreprise. Seuls l'administration et l'auteur de l'offre
        // peuvent la modifier.
        if (!IsAdmin() && !await _perimetre.PeutGerer(GetUserId(), job.CreatedByUserId))
            return Forbid();

        // Une offre reprise chez un partenaire n'est pas notre contenu.
        // La reecrire reviendrait a republier sous notre nom une annonce
        // alteree dont France Travail reste la source de verite : le
        // candidat lirait ici un intitule ou un salaire que l'employeur
        // n'a jamais ecrits, et l'annonce d'origine, toujours en ligne,
        // le contredirait.
        //
        // Le refus est pose ici et pas seulement dans l'interface : une
        // interdiction qui ne vit que dans un formulaire n'en est pas une.
        //
        // Retirer une annonce importee reste possible — elle se rejette en
        // moderation, ce qui la sort des recherches sans toucher a son
        // contenu.
        if (job.ExternalSource != null)
            return Conflict(new
            {
                message = $"Cette offre est reprise chez {job.ExternalSource} : son contenu ne se modifie pas ici. "
                        + "Pour la retirer des recherches, rejetez-la en moderation.",
            });

        job.Title = dto.Title;
        job.Company = dto.Company;
        job.Location = dto.Location;
        job.Description = dto.Description;
        job.ContractType = dto.ContractType;
        job.Salary = dto.Salary;
        job.Category = dto.Category;
        job.IsRemote = dto.IsRemote;
        job.ExpiresAt = dto.ExpiresAt;
        job.CompanyLogoUrl = dto.CompanyLogoUrl;
        job.Tags = dto.Tags;
        job.MinSalary = dto.MinSalary;
        job.MaxSalary = dto.MaxSalary;
        job.ExperienceRequired = dto.ExperienceRequired;
        job.EducationLevel = dto.EducationLevel;
        job.Benefits = dto.Benefits;
        job.WorkSchedule = dto.WorkSchedule;
        job.Languages = dto.Languages;
        job.CompanyDescription = dto.CompanyDescription;
        job.IsUrgent = dto.IsUrgent;
        job.EasyApply = dto.EasyApply;
        job.ScreeningQuestions = dto.ScreeningQuestions;
        job.AutoReplyMessage = dto.AutoReplyMessage;
        job.Openings = dto.Openings < 1 ? 1 : dto.Openings;
        job.WorkplaceType = dto.WorkplaceType;
        job.Address = dto.Address;
        job.SalaryPeriod = dto.SalaryPeriod;
        job.SupplementalPay = dto.SupplementalPay;
        job.ContractDuration = dto.ContractDuration;
        job.HoursPerWeek = dto.HoursPerWeek;
        job.StartDate = dto.StartDate;
        job.ApplicationEmail = dto.ApplicationEmail;
        job.RequireResume = dto.RequireResume;

        // Passage brouillon -> publie : l'offre reprend le cycle de vie normal
        // (duree d'affichage par defaut si aucune echeance n'a ete fixee).
        var wasDraft = job.IsDraft;
        job.IsDraft = dto.IsDraft;
        // Un brouillon etait inactif par construction : le publier doit le
        // rendre visible, quel que soit l'etat que porte le formulaire.
        EtatOffre.Appliquer(job, dto.IsDraft ? false : (wasDraft || dto.IsActive));
        // Un brouillon peut avoir dormi plus longtemps que sa duree d'affichage :
        // sans cette remise a zero, il serait retire des sa publication.
        if (wasDraft && !dto.IsDraft && (job.ExpiresAt == null || job.ExpiresAt < DateTime.UtcNow))
        {
            var durationStr = await _context.PlatformSettings
                .Where(s => s.Key == "default_offer_duration")
                .Select(s => s.Value)
                .FirstOrDefaultAsync();
            job.ExpiresAt = DateTime.UtcNow.AddDays(int.TryParse(durationStr, out var dd) ? dd : 30);
        }

        if (!string.IsNullOrWhiteSpace(job.WorkplaceType))
            job.IsRemote = job.WorkplaceType is "Télétravail" or "Hybride";

        // Re-geocodage du lieu (peut avoir change)
        var geo = lpdeBack.Services.GeoUtils.Geocode(job.Location);
        job.Latitude = geo?.Lat;
        job.Longitude = geo?.Lng;

        // Re-submit to moderation if moderation is enabled (admin bypass).
        // Un brouillon n'est pas concerne : rien n'est encore publie.
        if (!IsAdmin() && !job.IsDraft)
        {
            var requireModeration = await _context.PlatformSettings
                .Where(s => s.Key == "require_moderation")
                .Select(s => s.Value)
                .FirstOrDefaultAsync() == "true";

            if (requireModeration)
            {
                job.ModerationStatus = "Pending";
                job.ModerationNote = null;
                EtatOffre.Appliquer(job, false);
            }
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return NotFound();

        _context.JobOffers.Remove(job);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
