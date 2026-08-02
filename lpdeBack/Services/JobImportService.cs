using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Importe de vraies offres d'emploi depuis des API publiques
/// (Arbeitnow &amp; Remotive : sans clé ; France Travail : si configuré) vers le modèle JobOffer.
/// </summary>
public class JobImportService
{
    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<JobImportService> _logger;
    private readonly IMemoryCache _cache;
    private readonly QualiteCatalogue _qualite;

    /// <summary>
    /// Un seul import a la fois, tous points d'entree confondus. Le service est
    /// enregistre en Scoped : le verrou doit donc etre statique pour couvrir le
    /// timer de six heures et le declenchement admin, qui visent la meme base.
    ///
    /// Sans lui, deux passages simultanes constituent chacun leur liste de
    /// deduplication avant que l'autre n'ait insere quoi que ce soit, concluent
    /// tous deux que le catalogue est absent, et le reinserent en entier.
    /// </summary>
    private static readonly SemaphoreSlim ImportGate = new(1, 1);

    /// <summary>Vrai tant qu'un import est en cours, quel qu'en soit le declencheur.</summary>
    public static bool IsRunning { get; private set; }

    /// <summary>Issue d'un import : <c>started</c> est faux si un autre etait deja en cours.</summary>
    public record ImportOutcome(bool started, int added);

    public JobImportService(AppDbContext context, IHttpClientFactory httpFactory, IConfiguration config,
        ILogger<JobImportService> logger, IMemoryCache cache, QualiteCatalogue qualite)
    {
        _context = context;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
        _cache = cache;
        _qualite = qualite;
    }

    /// <summary>
    /// Compte les offres importees en double, sans rien modifier. L'exemplaire
    /// conserve serait le plus ancien Id de chaque groupe ; tous les autres sont
    /// comptes comme excedentaires.
    ///
    /// Les dependances portees par ces excedentaires sont comptees a part : leurs
    /// tables sont en suppression en cascade, donc une purge naive detruirait ces
    /// candidatures, favoris et signalements avec les lignes.
    /// </summary>
    public async Task<object> AnalyzeDuplicatesAsync(CancellationToken ct = default)
    {
        var imported = _context.JobOffers.Where(j => j.ExternalId != null);

        var total = await _context.JobOffers.CountAsync(ct);
        var withExternalId = await imported.CountAsync(ct);
        var distinctExternalIds = await imported.Select(j => j.ExternalId).Distinct().CountAsync(ct);

        var duplicatedGroups = await imported
            .GroupBy(j => j.ExternalId)
            .Where(g => g.Count() > 1)
            .CountAsync(ct);

        // Les survivants : un par ExternalId, le plus ancien.
        var survivorIds = imported.GroupBy(j => j.ExternalId).Select(g => g.Min(x => x.Id));
        var surplusIds = imported.Where(j => !survivorIds.Contains(j.Id)).Select(j => j.Id);

        var applicationsAtRisk = await _context.Applications.CountAsync(a => surplusIds.Contains(a.JobOfferId), ct);
        var notesAtRisk = await _context.JobNotes.CountAsync(n => surplusIds.Contains(n.JobOfferId), ct);
        var reportsAtRisk = await _context.JobReports.CountAsync(r => surplusIds.Contains(r.JobOfferId), ct);

        var sample = await imported
            .GroupBy(j => j.ExternalId)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new
            {
                externalId = g.Key,
                copies = g.Count(),
                keepId = g.Min(x => x.Id),
                ids = g.Select(x => x.Id).ToList(),
            })
            .ToListAsync(ct);

        return new
        {
            totalOffers = total,
            importedOffers = withExternalId,
            distinctExternalIds,
            duplicatedGroups,
            surplusRows = withExternalId - distinctExternalIds,
            atRisk = new
            {
                applications = applicationsAtRisk,
                favourites = notesAtRisk,
                reports = reportsAtRisk,
            },
            sample,
        };
    }

    /// <summary>Resultat d'une purge. <c>applied</c> est faux en simulation.</summary>
    public record PurgeOutcome(
        bool started, bool applied, int surplusRows, int encumberedRows,
        int applicationsReassigned, int reportsReassigned, int notesReassigned, int notesDeleted,
        int offersDeleted, int batches, string message);

    /// <summary>
    /// Supprime les exemplaires excedentaires des offres importees, en gardant le
    /// plus ancien Id de chaque ExternalId. Simulation par defaut.
    ///
    /// Partage le verrou de l'import : purger pendant qu'un import ecrit reviendrait
    /// a calculer les survivants sur une base qui bouge.
    /// </summary>
    public async Task<PurgeOutcome> PurgeDuplicatesAsync(bool apply, int batchSize, CancellationToken ct = default)
    {
        if (!await ImportGate.WaitAsync(0, ct))
        {
            _logger.LogWarning("Purge des doublons refusee : un import est en cours.");
            return new PurgeOutcome(false, false, 0, 0, 0, 0, 0, 0, 0, 0,
                "Un import est en cours. Reessayez une fois qu'il est termine.");
        }

        IsRunning = true;
        try { return await PurgeCoreAsync(apply, batchSize, ct); }
        finally { IsRunning = false; ImportGate.Release(); }
    }

    private async Task<PurgeOutcome> PurgeCoreAsync(bool apply, int batchSize, CancellationToken ct)
    {
        batchSize = Math.Clamp(batchSize, 100, 5000);

        var imported = _context.JobOffers.Where(j => j.ExternalId != null);
        var total = await _context.JobOffers.CountAsync(ct);
        var survivorIds = imported.GroupBy(j => j.ExternalId).Select(g => g.Min(x => x.Id));
        var surplusIds = await imported.Where(j => !survivorIds.Contains(j.Id)).Select(j => j.Id).ToListAsync(ct);

        if (surplusIds.Count == 0)
            return new PurgeOutcome(true, apply, 0, 0, 0, 0, 0, 0, 0, 0, "Aucun doublon a supprimer.");

        // Garde-fou : viser la quasi-totalite du catalogue trahirait une erreur de
        // raisonnement, pas une base a moitie dupliquee. On s'arrete plutot que de
        // vider la table sur un calcul faux.
        if (surplusIds.Count > total * 0.9)
            return new PurgeOutcome(true, false, surplusIds.Count, 0, 0, 0, 0, 0, 0, 0,
                $"Purge refusee : {surplusIds.Count} lignes visees sur {total}, soit la quasi-totalite du catalogue.");

        // Les lignes reellement grevees d'une dependance. Le comptage precedent en
        // annoncait zero, mais une candidature a pu arriver depuis : on revefifie
        // plutot que de faire confiance a une mesure vieille de quelques heures.
        var encumbered = new HashSet<int>();
        foreach (var chunk in surplusIds.Chunk(batchSize))
        {
            encumbered.UnionWith(await _context.Applications
                .Where(a => chunk.Contains(a.JobOfferId)).Select(a => a.JobOfferId).Distinct().ToListAsync(ct));
            encumbered.UnionWith(await _context.JobNotes
                .Where(n => chunk.Contains(n.JobOfferId)).Select(n => n.JobOfferId).Distinct().ToListAsync(ct));
            encumbered.UnionWith(await _context.JobReports
                .Where(r => chunk.Contains(r.JobOfferId)).Select(r => r.JobOfferId).Distinct().ToListAsync(ct));
        }

        // Correspondance excedentaire -> survivant, construite pour les seules
        // lignes grevees : inutile de la batir pour les cent mille autres.
        var toSurvivor = new Dictionary<int, int>();
        if (encumbered.Count > 0)
        {
            var rows = await _context.JobOffers
                .Where(j => encumbered.Contains(j.Id))
                .Select(j => new { j.Id, j.ExternalId })
                .ToListAsync(ct);

            var keys = rows.Select(r => r.ExternalId).Distinct().ToList();
            var survivors = await _context.JobOffers
                .Where(j => j.ExternalId != null && keys.Contains(j.ExternalId))
                .GroupBy(j => j.ExternalId)
                .Select(g => new { key = g.Key, id = g.Min(x => x.Id) })
                .ToListAsync(ct);

            var byKey = survivors.ToDictionary(s => s.key!, s => s.id);
            foreach (var r in rows)
                if (byKey.TryGetValue(r.ExternalId!, out var survivor)) toSurvivor[r.Id] = survivor;
        }

        if (!apply)
            return new PurgeOutcome(true, false, surplusIds.Count, encumbered.Count, 0, 0, 0, 0, 0, 0,
                $"Simulation : {surplusIds.Count} exemplaire(s) a supprimer, dont {encumbered.Count} portant une dependance a reaffecter. Rien n'a ete modifie.");

        int apps = 0, reports = 0, notesMoved = 0, notesDropped = 0;
        foreach (var (loser, survivor) in toSurvivor)
        {
            apps += await _context.Applications.Where(a => a.JobOfferId == loser)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.JobOfferId, survivor), ct);
            reports += await _context.JobReports.Where(r => r.JobOfferId == loser)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.JobOfferId, survivor), ct);

            // Favoris : (UserId, JobOfferId) est unique. Si la personne a deja le
            // survivant en favori, reaffecter violerait l'index — on supprime le
            // favori en double au lieu de le deplacer.
            var deja = await _context.JobNotes.Where(n => n.JobOfferId == survivor)
                .Select(n => n.UserId).ToListAsync(ct);
            notesDropped += await _context.JobNotes
                .Where(n => n.JobOfferId == loser && deja.Contains(n.UserId)).ExecuteDeleteAsync(ct);
            notesMoved += await _context.JobNotes.Where(n => n.JobOfferId == loser)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.JobOfferId, survivor), ct);
        }

        // Suppression par lots, hors transaction d'ensemble : une transaction de
        // cent mille lignes tiendrait la table trop longtemps. Une interruption
        // laisse un travail partiel, que relancer la purge termine — l'operation
        // est idempotente.
        int deleted = 0, batches = 0;
        foreach (var chunk in surplusIds.Chunk(batchSize))
        {
            deleted += await _context.JobOffers.Where(j => chunk.Contains(j.Id)).ExecuteDeleteAsync(ct);
            batches++;
        }

        BrowseCache.Invalidate(_cache);
        _logger.LogWarning("Purge des doublons : {Deleted} offres supprimees en {Batches} lots.", deleted, batches);

        return new PurgeOutcome(true, true, surplusIds.Count, encumbered.Count,
            apps, reports, notesMoved, notesDropped, deleted, batches,
            $"{deleted} exemplaire(s) en double supprime(s).");
    }

    // Diagnostic : vérifie la configuration et la réponse des sources à clé (sans exposer les clés).
    public async Task<object> DiagnoseAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, object>();
        var http = _httpFactory.CreateClient();

        var aId = _config["Adzuna:AppId"]; var aKey = _config["Adzuna:AppKey"];
        if (string.IsNullOrWhiteSpace(aId) || string.IsNullOrWhiteSpace(aKey))
            result["adzuna"] = new { configured = false };
        else
        {
            try
            {
                var country = _config["Adzuna:Country"] ?? "fr";
                var url = $"https://api.adzuna.com/v1/api/jobs/{country}/search/1?app_id={aId}&app_key={aKey}&results_per_page=5&content-type=application/json";
                var resp = await http.GetAsync(url, ct);
                var body = Encoding.UTF8.GetString(await resp.Content.ReadAsByteArrayAsync(ct));
                int count = 0;
                try { using var d = JsonDocument.Parse(body); if (d.RootElement.TryGetProperty("results", out var r)) count = r.GetArrayLength(); } catch { }
                result["adzuna"] = new { configured = true, status = (int)resp.StatusCode, results = count, error = resp.IsSuccessStatusCode ? null : Trunc(body, 300) };
            }
            catch (Exception ex) { result["adzuna"] = new { configured = true, error = ex.Message }; }
        }

        var jKey = _config["Jooble:ApiKey"];
        if (string.IsNullOrWhiteSpace(jKey))
            result["jooble"] = new { configured = false };
        else
        {
            try
            {
                var jbody = "{\"keywords\":\"\",\"location\":\"France\",\"page\":\"1\"}";
                var req = new HttpRequestMessage(HttpMethod.Post, $"https://jooble.org/api/{jKey}") { Content = new StringContent(jbody, Encoding.UTF8, "application/json") };
                var resp = await http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                int count = 0;
                try { using var d = JsonDocument.Parse(body); if (d.RootElement.TryGetProperty("jobs", out var jj)) count = jj.GetArrayLength(); } catch { }
                result["jooble"] = new { configured = true, status = (int)resp.StatusCode, jobs = count, error = resp.IsSuccessStatusCode ? null : Trunc(body, 300) };
            }
            catch (Exception ex) { result["jooble"] = new { configured = true, error = ex.Message }; }
        }
        return result;
    }

    /// <summary>
    /// Importe les offres, sauf si un import tourne deja : dans ce cas le passage
    /// est abandonne plutot que mis en attente. Deux imports a la suite n'ont
    /// aucun interet, et faire patienter le second rouvrirait la fenetre de
    /// course des que le premier relacherait le verrou.
    /// </summary>
    public async Task<ImportOutcome> ImportAllAsync(CancellationToken ct = default)
    {
        if (!await ImportGate.WaitAsync(0, ct))
        {
            _logger.LogWarning("Import deja en cours : ce declenchement est ignore.");
            return new ImportOutcome(false, 0);
        }

        IsRunning = true;
        try
        {
            return new ImportOutcome(true, await ImportAllCoreAsync(ct));
        }
        finally
        {
            IsRunning = false;
            ImportGate.Release();
        }
    }

    private async Task<int> ImportAllCoreAsync(CancellationToken ct)
    {
        var existing = await _context.JobOffers
            .Where(j => j.ExternalId != null)
            .Select(j => j.ExternalId!)
            .ToListAsync(ct);
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        // ── Sources d'import ──
        // Seule France Travail alimente le catalogue. Les agregateurs
        // Arbeitnow, Remotive, Adzuna et Jooble sont desactives : leurs
        // offres ont ete retirees, et les laisser tourner les ferait
        // revenir au prochain passage — la suppression se deferait seule.
        //
        // Leurs methodes de recuperation restent en place : les rallumer
        // ne demande que de retablir la ligne correspondante.
        var toAdd = new List<JobOffer>();
        try { toAdd.AddRange(await FetchFranceTravailAsync(seen, ct)); } catch (Exception e) { _logger.LogWarning(e, "Import France Travail échoué"); }

        // ── Ce qui existe deja sous un autre identifiant ──
        //
        // « ExternalId » dedoublonne au sein d'une source ; il ne voit
        // rien entre elles. La meme annonce arrive de France Travail,
        // d'Adzuna et de Jooble avec trois identifiants differents, et
        // le candidat voit trois fois le meme poste — puis postule deux
        // fois par erreur.
        //
        // L'empreinte porte sur ce qui identifie un poste : intitule
        // normalise, entreprise, ville. On la calcule pour chaque offre
        // entrante, on ecarte celles qui existent deja, et surtout on
        // rafraichit la date de derniere vue de l'ancienne : c'est elle
        // qui la maintient en vie face a l'expiration.
        var deja = await _context.JobOffers
            .Where(j => j.IsActive && j.Empreinte != null)
            .Select(j => new { j.Id, j.Empreinte })
            .ToListAsync(ct);

        var parEmpreinte = deja
            .GroupBy(x => x.Empreinte!)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Id));

        var retenues = new List<JobOffer>(toAdd.Count);
        var revues = new List<int>();
        var dansCeLot = new HashSet<string>();
        var suspectes = 0;

        foreach (var offre in toAdd)
        {
            var empreinte = QualiteCatalogue.Empreinte(offre.Title, offre.Company, offre.Location);
            offre.Empreinte = empreinte;
            offre.VueChezLaSourceLe = DateTime.UtcNow;

            if (parEmpreinte.TryGetValue(empreinte, out var ancienneId))
            {
                revues.Add(ancienneId);
                continue;
            }

            // Le meme lot peut contenir deux fois la meme annonce : deux
            // pages de resultats qui se chevauchent chez la source.
            if (!dansCeLot.Add(empreinte)) continue;

            if (_qualite.Filtrer(offre)) suspectes++;
            retenues.Add(offre);
        }

        if (revues.Count > 0)
        {
            // Par lots : une clause IN de dix mille identifiants fait
            // echouer SQL Server, qui plafonne les parametres.
            foreach (var lot in revues.Distinct().Chunk(1_000))
            {
                await _context.JobOffers
                    .Where(j => lot.Contains(j.Id))
                    .ExecuteUpdateAsync(m => m.SetProperty(j => j.VueChezLaSourceLe, DateTime.UtcNow), ct);
            }
        }

        if (toAdd.Count != retenues.Count)
            _logger.LogInformation(
                "Dedoublonnage : {Ecartees} offres deja presentes sous un autre identifiant, {Revues} rafraichies.",
                toAdd.Count - retenues.Count, revues.Distinct().Count());

        if (suspectes > 0)
            _logger.LogWarning("{Nombre} offres importees mises en file de moderation par l'analyse.", suspectes);

        toAdd = retenues;

        if (toAdd.Count == 0) return 0;

        // Insertion par lots, résiliente : si un lot échoue (une offre au format
        // problématique), on réessaie offre par offre pour isoler et ignorer la
        // mauvaise, sans faire échouer tout l'import.
        const int batch = 500;
        int saved = 0, failed = 0;
        for (int i = 0; i < toAdd.Count; i += batch)
        {
            var slice = toAdd.GetRange(i, Math.Min(batch, toAdd.Count - i));
            try
            {
                _context.JobOffers.AddRange(slice);
                await _context.SaveChangesAsync(ct);
                foreach (var o in slice) _context.Entry(o).State = EntityState.Detached;
                saved += slice.Count;
            }
            catch (Exception exBatch)
            {
                _logger.LogWarning(exBatch, "Lot d'import échoué ({Count} offres) — reprise unitaire", slice.Count);
                DetachAll();
                foreach (var o in slice)
                {
                    try
                    {
                        _context.JobOffers.Add(o);
                        await _context.SaveChangesAsync(ct);
                        _context.Entry(o).State = EntityState.Detached;
                        saved++;
                    }
                    catch (Exception exOne)
                    {
                        DetachAll();
                        failed++;
                        _logger.LogWarning(exOne, "Offre ignorée [{Source}] {Ext} — {Title}", o.ExternalSource, o.ExternalId, o.Title);
                    }
                }
            }
        }
        _logger.LogInformation("Import: {Saved} offres ajoutées, {Failed} ignorées.", saved, failed);
        return saved;

        void DetachAll()
        {
            foreach (var entry in _context.ChangeTracker.Entries().ToList())
                entry.State = EntityState.Detached;
        }
    }

    // ── Arbeitnow (gratuit, sans clé) ──
    private async Task<List<JobOffer>> FetchArbeitnowAsync(HashSet<string> seen, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        var json = await http.GetStringAsync("https://www.arbeitnow.com/api/job-board-api", ct);
        using var doc = JsonDocument.Parse(json);
        var list = new List<JobOffer>();

        foreach (var e in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var slug = Str(e, "slug");
            if (slug == null) continue;
            var ext = "arbeitnow:" + slug;
            if (!seen.Add(ext)) continue;

            var title = Str(e, "title") ?? "Offre";
            var tags = e.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString()).Where(x => x != null).Take(6).ToList()
                : new List<string?>();
            var jobType = e.TryGetProperty("job_types", out var jt) && jt.ValueKind == JsonValueKind.Array && jt.GetArrayLength() > 0
                ? jt[0].GetString() : null;
            var remote = e.TryGetProperty("remote", out var r) && r.ValueKind == JsonValueKind.True;

            list.Add(BuildOffer(ext, "arbeitnow", title, Str(e, "company_name") ?? "Entreprise",
                Str(e, "location") ?? (remote ? "Télétravail" : ""), StripHtml(Str(e, "description")),
                MapContract(jobType), GuessCategory(title, tags), remote,
                string.Join(", ", tags), null, Str(e, "url")));
        }
        return list;
    }

    // ── Remotive (gratuit, sans clé) ──
    private async Task<List<JobOffer>> FetchRemotiveAsync(HashSet<string> seen, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        var json = await http.GetStringAsync("https://remotive.com/api/remote-jobs?limit=100", ct);
        using var doc = JsonDocument.Parse(json);
        var list = new List<JobOffer>();

        foreach (var e in doc.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var id = e.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
            if (id == null) continue;
            var ext = "remotive:" + id;
            if (!seen.Add(ext)) continue;

            var title = Str(e, "title") ?? "Offre";
            var tags = e.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString()).Where(x => x != null).Take(6).ToList()
                : new List<string?>();

            list.Add(BuildOffer(ext, "remotive", title, Str(e, "company_name") ?? "Entreprise",
                Str(e, "candidate_required_location") ?? "Télétravail", StripHtml(Str(e, "description")),
                MapContract(Str(e, "job_type")), GuessCategory(title, tags) is "Autre" ? MapCategory(Str(e, "category")) : GuessCategory(title, tags),
                true, string.Join(", ", tags), NullIfEmpty(Str(e, "salary")), Str(e, "url")));
        }
        return list;
    }

    // ── Adzuna (agrégateur, si configuré : Adzuna:AppId / Adzuna:AppKey) ──
    private async Task<List<JobOffer>> FetchAdzunaAsync(HashSet<string> seen, CancellationToken ct)
    {
        var appId = _config["Adzuna:AppId"];
        var appKey = _config["Adzuna:AppKey"];
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appKey))
            return new List<JobOffer>(); // non configuré → ignoré

        var country = _config["Adzuna:Country"] ?? "fr";
        int maxPages = int.TryParse(_config["Adzuna:MaxPages"], out var mp) && mp > 0 ? mp : 20;
        const int perPage = 50;

        var http = _httpFactory.CreateClient();
        var list = new List<JobOffer>();

        for (int page = 1; page <= maxPages; page++)
        {
            var url = $"https://api.adzuna.com/v1/api/jobs/{country}/search/{page}"
                + $"?app_id={appId}&app_key={appKey}&results_per_page={perPage}&content-type=application/json";
            string json;
            try
            {
                // Adzuna renvoie un charset invalide dans Content-Type : on lit les octets
                // et on décode en UTF-8 nous-mêmes (GetStringAsync échouerait).
                var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) { _logger.LogWarning("Adzuna page {Page} ({Code})", page, resp.StatusCode); break; }
                json = Encoding.UTF8.GetString(await resp.Content.ReadAsByteArrayAsync(ct));
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Adzuna page {Page} échouée", page); break; }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) break;

            foreach (var e in results.EnumerateArray())
            {
                var id = e.TryGetProperty("id", out var idEl) ? (idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.ToString()) : null;
                if (id == null) continue;
                var ext = "adzuna:" + id;
                if (!seen.Add(ext)) continue;

                var title = Str(e, "title") ?? "Offre";
                string company = e.TryGetProperty("company", out var comp) ? (Str(comp, "display_name") ?? "Entreprise") : "Entreprise";
                string location = e.TryGetProperty("location", out var loc) ? (Str(loc, "display_name") ?? "") : "";
                string category = e.TryGetProperty("category", out var cat) ? (Str(cat, "label") ?? "Autre") : "Autre";
                var contract = MapAdzunaContract(Str(e, "contract_time"), Str(e, "contract_type"));
                var url2 = Str(e, "redirect_url");

                var offer = BuildOffer(ext, "adzuna", title, company, location, StripHtml(Str(e, "description")),
                    contract, GuessCategory(title, new()) is "Autre" ? category : GuessCategory(title, new()),
                    false, null, null, url2);
                offer.Latitude = Num(e, "latitude"); offer.Longitude = Num(e, "longitude");
                var (aMin, aMax) = NormalizeToAnnual(Num(e, "salary_min"), Num(e, "salary_max"));
                offer.MinSalary = aMin; offer.MaxSalary = aMax;
                if (aMin.HasValue || aMax.HasValue)
                    offer.Salary = Trunc($"{(aMin ?? aMax):n0} – {(aMax ?? aMin):n0} € / an", 120);
                list.Add(offer);
            }
            if (results.GetArrayLength() < perPage) break; // dernière page
        }
        _logger.LogInformation("Adzuna : {Count} offres importées.", list.Count);
        return list;
    }

    // ── Jooble (agrégateur, si configuré : Jooble:ApiKey) ──
    private async Task<List<JobOffer>> FetchJoobleAsync(HashSet<string> seen, CancellationToken ct)
    {
        var apiKey = _config["Jooble:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return new List<JobOffer>(); // non configuré → ignoré

        var location = _config["Jooble:Location"] ?? "France";
        var keywords = _config["Jooble:Keywords"] ?? "";
        int maxPages = int.TryParse(_config["Jooble:MaxPages"], out var mp) && mp > 0 ? mp : 10;

        var http = _httpFactory.CreateClient();
        var list = new List<JobOffer>();

        for (int page = 1; page <= maxPages; page++)
        {
            var body = $"{{\"keywords\":\"{keywords}\",\"location\":\"{location}\",\"page\":\"{page}\",\"ResultOnPage\":20}}";
            var req = new HttpRequestMessage(HttpMethod.Post, $"https://jooble.org/api/{apiKey}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            HttpResponseMessage resp;
            try { resp = await http.SendAsync(req, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Jooble page {Page} échouée", page); break; }
            if (!resp.IsSuccessStatusCode) { _logger.LogWarning("Jooble: page {Page} ({Code})", page, resp.StatusCode); break; }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("jobs", out var jobs) || jobs.GetArrayLength() == 0) break;

            foreach (var e in jobs.EnumerateArray())
            {
                var link = Str(e, "link");
                var id = e.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null
                    ? idEl.ToString()
                    : (link != null ? Math.Abs(link.GetHashCode()).ToString() : null);
                if (id == null) continue;
                var ext = "jooble:" + id;
                if (!seen.Add(ext)) continue;

                var title = Str(e, "title") ?? "Offre";
                var contract = MapContract(Str(e, "type"));
                var (jmin, jmax) = ParseFtSalary(Str(e, "salary")); // même parseur (texte libre)

                var offer = BuildOffer(ext, "jooble", title, NullIfEmpty(Str(e, "company")) ?? "Entreprise",
                    Str(e, "location") ?? "", StripHtml(Str(e, "snippet")),
                    contract, GuessCategory(title, new()), false, null, NullIfEmpty(Str(e, "salary")), link);
                offer.MinSalary = jmin; offer.MaxSalary = jmax;
                list.Add(offer);
            }
            if (jobs.GetArrayLength() < 20) break; // dernière page
        }
        _logger.LogInformation("Jooble : {Count} offres importées.", list.Count);
        return list;
    }

    // ── France Travail (officiel FR, si configuré) ──
    private async Task<List<JobOffer>> FetchFranceTravailAsync(HashSet<string> seen, CancellationToken ct)
    {
        var clientId = _config["FranceTravail:ClientId"];
        var clientSecret = _config["FranceTravail:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return new List<JobOffer>(); // non configuré → ignoré

        var http = _httpFactory.CreateClient();

        // 1) Jeton OAuth2 (client_credentials)
        var tokenReq = new HttpRequestMessage(HttpMethod.Post,
            "https://entreprise.francetravail.fr/connexion/oauth2/access_token?realm=%2Fpartenaire")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "api_offresdemploiv2 o2dsoffre",
            }),
        };
        var tokenResp = await http.SendAsync(tokenReq, ct);
        if (!tokenResp.IsSuccessStatusCode) { _logger.LogWarning("France Travail: token refusé ({Code})", tokenResp.StatusCode); return new(); }
        using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync(ct));
        var token = tokenDoc.RootElement.GetProperty("access_token").GetString();

        // 2) Recherche d'offres — paginée (150/appel) + balayage par département.
        //    L'API plafonne une même recherche à ~1150 résultats ; on balaie donc
        //    tous les départements pour dépasser cette limite. Plafond configurable.
        var list = new List<JobOffer>();
        // Par défaut : le maximum possible. Réglable via FranceTravail:MaxOffers.
        int cap = int.TryParse(_config["FranceTravail:MaxOffers"], out var m) && m > 0 ? m : 100_000;
        const string baseUrl = "https://api.francetravail.io/partenaire/offresdemploi/v2/offres/search";

        // Mappe une page de résultats ; renvoie le nombre d'éléments reçus (avant dédup).
        async Task<int> FetchPageAsync(string? dept, int start)
        {
            var url = $"{baseUrl}?range={start}-{start + 149}" + (dept != null ? $"&departement={dept}" : "");
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await http.SendAsync(req, ct);
            // 200 OK ou 206 Partial Content = succès ; 204 = plus de résultats.
            if (!resp.IsSuccessStatusCode) { _logger.LogWarning("France Travail: page {Dept}/{Start} échouée ({Code})", dept, start, resp.StatusCode); return 0; }
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return 0;
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("resultats", out var results)) return 0;

            int received = 0;
            foreach (var e in results.EnumerateArray())
            {
                received++;
                var id = Str(e, "id");
                if (id == null) continue;
                var ext = "francetravail:" + id;
                if (!seen.Add(ext)) continue;

                var title = Str(e, "intitule") ?? "Offre";
                string company = e.TryGetProperty("entreprise", out var ent) ? (Str(ent, "nom") ?? "Entreprise") : "Entreprise";
                string location = ""; double? lat = null, lng = null;
                if (e.TryGetProperty("lieuTravail", out var lieu))
                {
                    location = Str(lieu, "libelle") ?? "";
                    if (lieu.TryGetProperty("latitude", out var la) && la.ValueKind == JsonValueKind.Number) lat = la.GetDouble();
                    if (lieu.TryGetProperty("longitude", out var lo) && lo.ValueKind == JsonValueKind.Number) lng = lo.GetDouble();
                }
                string? salary = e.TryGetProperty("salaire", out var sal) ? NullIfEmpty(Str(sal, "libelle")) : null;
                var contract = MapFtContract(Str(e, "typeContrat"), Str(e, "typeContratLibelle"));
                var category = GuessCategory(title, new()) is "Autre" ? (Str(e, "romeLibelle") ?? "Autre") : GuessCategory(title, new());

                string? ftUrl = null;
                if (e.TryGetProperty("contact", out var contact)) ftUrl = Str(contact, "urlPostulation");
                if (ftUrl == null && e.TryGetProperty("origineOffre", out var origine)) ftUrl = Str(origine, "urlOrigine");
                ftUrl ??= $"https://candidat.francetravail.fr/offres/recherche/detail/{id}";

                var offer = BuildOffer(ext, "francetravail", title, company, location, StripHtml(Str(e, "description")),
                    contract, category, false, null, salary, ftUrl);
                offer.Latitude = lat; offer.Longitude = lng;
                offer.WorkSchedule = MapFtSchedule(Str(e, "dureeTravailLibelleConverti"));
                offer.ExperienceRequired = MapFtExperience(Str(e, "experienceExige"));
                offer.EducationLevel = MapFtEducation(e);
                offer.Languages = MapFtLanguages(e);
                var (smin, smax) = ParseFtSalary(salary); // salaire chiffré (annuel €) pour filtres/tri
                offer.MinSalary = smin; offer.MaxSalary = smax;
                list.Add(offer);
            }
            return received;
        }

        // a) Recherche nationale paginée (jusqu'au plafond API de ~1150).
        for (int start = 0; start < 1150 && list.Count < cap; start += 150)
        {
            var got = await FetchPageAsync(null, start);
            if (got < 150) break; // dernière page atteinte
        }

        // b) Balayage par département pour dépasser la limite d'une recherche unique.
        if (list.Count < cap)
        {
            foreach (var dept in FrenchDepartments)
            {
                if (list.Count >= cap) break;
                // On paginte chaque département jusqu'au plafond API (~1150).
                for (int start = 0; start < 1150 && list.Count < cap; start += 150)
                {
                    var got = await FetchPageAsync(dept, start);
                    if (got < 150) break;
                }
            }
        }

        _logger.LogInformation("France Travail: {Count} offres importées (plafond {Cap})", list.Count, cap);
        return list;
    }

    // Départements métropolitains (hors 20 → 2A/2B) + DOM.
    private static readonly string[] FrenchDepartments = BuildDepartments();
    private static string[] BuildDepartments()
    {
        var d = new List<string>();
        for (int i = 1; i <= 95; i++) { if (i == 20) continue; d.Add(i.ToString("D2")); }
        d.Add("2A"); d.Add("2B");
        d.AddRange(new[] { "971", "972", "973", "974", "976" });
        return d.ToArray();
    }

    // ── Construction & helpers ──
    private JobOffer BuildOffer(string ext, string source, string title, string company, string location,
        string description, string contract, string category, bool remote, string? tags, string? salary, string? externalUrl)
    {
        var geo = string.IsNullOrEmpty(location) ? null : GeoUtils.Geocode(location);
        return new JobOffer
        {
            ExternalId = ext,
            ExternalSource = source,
            ExternalUrl = externalUrl != null ? Trunc(externalUrl, 500) : null,
            Title = Trunc(title, 200),
            Company = Trunc(company, 100),
            Location = Trunc(location, 100),
            Description = string.IsNullOrWhiteSpace(description) ? "Voir le détail de l'offre." : description,
            ContractType = contract,
            Category = Trunc(category, 100),
            IsRemote = remote,
            Salary = salary != null ? Trunc(salary, 120) : null,
            Tags = tags != null ? Trunc(tags, 500) : null,
            IsActive = true,
            ModerationStatus = "Approved",
            EasyApply = true,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(45),
            Latitude = geo?.Lat,
            Longitude = geo?.Lng,
        };
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = Regex.Replace(html, "<(br|/p|/li|/div)[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "[ \t]+", " ");
        text = Regex.Replace(text, "\n{3,}", "\n\n").Trim();
        return text.Length > 2500 ? text[..2500] : text;
    }

    private static string MapContract(string? raw)
    {
        var s = (raw ?? "").ToLowerInvariant();
        if (s.Contains("intern") || s.Contains("stage")) return "Stage";
        if (s.Contains("apprentic") || s.Contains("altern")) return "Alternance";
        if (s.Contains("freelance") || s.Contains("contract") || s.Contains("indep")) return "Freelance";
        if (s.Contains("part")) return "CDD";
        if (s.Contains("temporary") || s.Contains("cdd")) return "CDD";
        return "CDI";
    }

    private static string MapFtContract(string? code, string? libelle)
    {
        var c = (code ?? "").ToUpperInvariant();
        var l = (libelle ?? "").ToLowerInvariant();
        if (l.Contains("altern") || l.Contains("apprentis") || c is "DDI") return "Alternance";
        if (l.Contains("stage") || l.Contains("saisonn")) return "Stage";
        if (c is "CDD" or "MIS" or "SAI" or "TTI") return "CDD";
        if (l.Contains("libér") || l.Contains("franchis") || c is "LIB") return "Freelance";
        return "CDI";
    }

    // Adzuna : contract_time (full_time/part_time) + contract_type (permanent/contract).
    private static string MapAdzunaContract(string? time, string? type)
    {
        var ty = (type ?? "").ToLowerInvariant();
        if (ty.Contains("contract")) return "CDD";
        if (ty.Contains("permanent")) return "CDI";
        var ti = (time ?? "").ToLowerInvariant();
        if (ti.Contains("part")) return "CDD";
        return "CDI";
    }

    // Lit une propriété numérique JSON (null si absente/non numérique).
    private static double? Num(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    // Horaires → vocabulaire des filtres ("Temps plein" / "Temps partiel").
    private static string? MapFtSchedule(string? libelle)
    {
        if (string.IsNullOrWhiteSpace(libelle)) return null;
        return libelle.ToLowerInvariant().Contains("partiel") ? "Temps partiel" : "Temps plein";
    }

    // experienceExige : D=Débutant, S=Souhaitée, E=Exigée → niveaux du filtre.
    private static string? MapFtExperience(string? code) => (code ?? "").ToUpperInvariant() switch
    {
        "D" => "Junior",
        "S" => "Intermediaire",
        "E" => "Senior",
        _ => null,
    };

    // formations[].niveauLibelle → niveaux du filtre.
    private static string? MapFtEducation(JsonElement e)
    {
        if (!e.TryGetProperty("formations", out var fms) || fms.ValueKind != JsonValueKind.Array) return null;
        foreach (var f in fms.EnumerateArray())
        {
            var nl = (Str(f, "niveauLibelle") ?? "").ToLowerInvariant();
            if (nl.Contains("doctorat") || nl.Contains("bac+8") || nl.Contains("bac + 8")) return "Doctorat";
            if (nl.Contains("bac+5") || nl.Contains("bac + 5")) return "Bac+5";
            if (nl.Contains("bac+3") || nl.Contains("bac + 3")) return "Bac+3";
            if (nl.Contains("bac+2") || nl.Contains("bac + 2")) return "Bac+2";
            if (nl.Contains("bac")) return "Bac";
        }
        return null;
    }

    // langues[].libelle → liste "Anglais, Espagnol".
    private static string? MapFtLanguages(JsonElement e)
    {
        if (!e.TryGetProperty("langues", out var lg) || lg.ValueKind != JsonValueKind.Array) return null;
        var langs = lg.EnumerateArray()
            .Select(x => Str(x, "libelle"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct()
            .ToList();
        return langs.Count > 0 ? string.Join(", ", langs) : null;
    }

    // Parse un libellé de salaire France Travail en fourchette ANNUELLE (€).
    // Ex : "Mensuel de 1800.0 Euros à 2200.0 Euros sur 12.0 mois" -> (21600, 26400)
    //      "Annuel de 30000.0 Euros sur 12.0 mois"               -> (30000, 30000)
    //      "Horaire de 11.65 Euros à 13.0 Euros"                 -> (~21203, ~23660)
    //
    // ── Ce qui produisait des salaires faux ──
    //
    // Quatre défauts se cumulaient, tous visibles sur la page publique des
    // salaires, qui classe les métiers par rémunération décroissante et
    // mettait donc les erreurs en tête de liste :
    //
    // 1. « Cachet de 120.0 Euros » n'a pas de période. Le repli sur l'ordre
    //    de grandeur lisait 120 comme un taux horaire : 120 × 35 × 52 =
    //    218 400 €. Un cachet d'artiste devenait le deuxième salaire de
    //    France. Un cachet, une prime ou un forfait ne se convertissent pas
    //    en salaire annuel : ils ne sont pas périodiques.
    //
    // 2. La plage mensuelle plausible montait à 30 000 €, soit 360 000 €
    //    par an. « Mensuel de 19050 Euros » pour un conducteur de ligne —
    //    une coquille de France Travail pour 1 905 € — passait donc le
    //    filtre et ressortait à 228 600 €. Le plafond descend à 15 000 €.
    //
    // 3. « Annuel de 0.0 Euros à 200000.0 Euros » : le zéro était écarté
    //    comme montant nul, et l'unique valeur restante servait de plancher
    //    ET de plafond. Une fourchette ouverte devenait un salaire ferme de
    //    200 000 €. Un plancher à zéro veut dire « non précisé » : le
    //    plancher reste nul.
    //
    // 4. « Annuel de 200000 à 500000 Euros » : 500 000 dépassait le plafond
    //    et était écarté, laissant 200 000 en plancher et en plafond. Écarter
    //    un montant ne doit pas resserrer la fourchette sur ce qui reste :
    //    le plafond devient inconnu, pas égal au plancher.
    public static (int? Min, int? Max) ParseFtSalary(string? libelle)
    {
        if (string.IsNullOrWhiteSpace(libelle)) return (null, null);
        var l = libelle.ToLowerInvariant();

        bool hasAnnuel = l.Contains("annuel");
        bool hasMensuel = l.Contains("mensuel");
        bool hasHoraire = l.Contains("horaire");

        // (1) Rémunérations non périodiques : sans période explicite, on ne
        // sait pas sur combien de fois le montant se répète — donc on ne
        // l'annualise pas.
        if (!hasAnnuel && !hasMensuel && !hasHoraire
            && Regex.IsMatch(l, @"\b(cachet|forfait|prime|commission|indemnit)"))
            return (null, null);

        // Montants suivis de "euro(s)" ou "€" (ignore "12 mois", "35 h", etc.).
        // Les zéros sont conservés ici : un « de 0 à X » n'est pas un montant
        // nul, c'est une borne basse non précisée, et le distinguer d'une
        // absence de borne change le résultat (voir 3).
        var raw = Regex.Matches(l, @"(\d+(?:[.,]\d+)?)\s*(?:euros?|€)")
            .Select(mt => ParseNum(mt.Groups[1].Value))
            .Where(d => d.HasValue).Select(d => d!.Value)
            .ToList();
        if (raw.Count == 0) return (null, null);

        bool floorDeclaredZero = raw.Count > 1 && raw[0] == 0;
        var amounts = raw.Where(d => d > 0).ToList();
        if (amounts.Count == 0) return (null, null);

        // Nombre de mois de versement (13e/14e mois éventuel), défaut 12.
        double months = 12;
        var mm = Regex.Match(l, @"sur\s+(\d+(?:[.,]\d+)?)\s*mois");
        if (mm.Success && ParseNum(mm.Groups[1].Value) is double mv && mv is >= 12 and <= 16) months = mv;

        // Selon la période, seuls les montants dans une plage plausible sont retenus :
        // certains libellés FT contiennent un montant annexe aberrant.
        double factor, lo, hi;
        if (hasAnnuel) { factor = 1; lo = 8_000; hi = 250_000; }
        // (2) 15 000 €/mois = 180 000 €/an : au-delà, un libellé « mensuel »
        // est une coquille bien plus souvent qu'une rémunération réelle.
        else if (hasMensuel) { factor = months; lo = 400; hi = 15_000; }
        else if (hasHoraire) { factor = 35 * 52; lo = 3; hi = 100; } // 35 h/sem légales
        else
        {
            // Période absente : on déduit de l'ordre de grandeur du plus grand montant.
            var big = amounts.Max();
            if (big >= 10_000) { factor = 1; lo = 8_000; hi = 250_000; }
            else if (big >= 500) { factor = months; lo = 400; hi = 15_000; }
            else { factor = 35 * 52; lo = 3; hi = 100; }
        }

        var kept = amounts.Where(a => a >= lo && a <= hi).ToList();
        if (kept.Count == 0) return (null, null);

        int aMin = (int)Math.Round(kept.Min() * factor);
        int aMax = (int)Math.Round(kept.Max() * factor);

        // (4) Un montant écarté par le haut ne doit pas faire retomber le
        // plafond sur le plancher : la borne haute devient inconnue, et
        // l'offre s'affiche « à partir de ».
        bool droppedAbove = amounts.Any(a => a > hi);
        if (droppedAbove && kept.Count < amounts.Count)
            return (aMin >= 1_000 ? aMin : (int?)null, null);

        // (3) Plancher annoncé à zéro : la borne basse reste inconnue.
        if (floorDeclaredZero) return (null, aMax is >= 1_000 and <= 250_000 ? aMax : (int?)null);

        if (aMax < 1000 || aMax > 250_000) return (null, null); // garde-fou plausibilité (plafond 250k)
        return (aMin, aMax);
    }

    private static double? ParseNum(string s) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    // Normalise une fourchette chiffrée (ex. Adzuna) en salaire ANNUEL (€).
    // Adzuna renvoie parfois des montants horaires/mensuels : on déduit l'échelle
    // de l'ordre de grandeur (basé sur le plus grand montant) et on l'applique
    // au min et au max de façon cohérente.
    private static (int? Min, int? Max) NormalizeToAnnual(double? smin, double? smax)
    {
        double basis = Math.Max(smin ?? 0, smax ?? 0);
        if (basis <= 0) return (null, null);

        double factor = basis >= 10_000 ? 1        // déjà annuel
            : basis >= 500 ? 12                     // mensuel
            : 35 * 52;                              // horaire (35 h/sem légales)

        int? Conv(double? v) => v is > 0 ? (int)Math.Round(v.Value * factor) : (int?)null;
        var aMin = Conv(smin);
        var aMax = Conv(smax);
        int hi = aMax ?? aMin ?? 0, lo = aMin ?? aMax ?? 0;
        if (hi < 1000 || hi > 250_000) return (null, null); // garde-fou plausibilité (plafond 250k)
        return (aMin, aMax);
    }

    /// <summary>
    /// Version de l'analyseur de salaires.
    ///
    /// À incrémenter dès que ParseFtSalary change de comportement. Le
    /// service de fond compare cette valeur à celle enregistrée en base et
    /// recalcule tout le corpus quand elles diffèrent.
    ///
    /// Sans ce numéro, une correction de l'analyseur ne valait que pour les
    /// offres importées ensuite : les anciennes gardaient indéfiniment la
    /// valeur fausse, et seul un bouton d'administration — exigeant des
    /// identifiants que personne n'a sous la main — pouvait les reprendre.
    /// Le code et les données divergeaient donc en silence.
    /// </summary>
    public const int VersionAnalyseSalaire = 2;

    /// <summary>La clé qui garde en base la version déjà appliquée.</summary>
    public const string CleVersionSalaire = "reparse_salaires_version";

    // Rétro-remplit le salaire chiffré des offres importées déjà en base (parcours par curseur d'Id).
    public async Task<int> ReparseSalariesAsync(bool force = false, CancellationToken ct = default)
    {
        int updated = 0, lastId = 0;
        const int take = 1000;
        while (!ct.IsCancellationRequested)
        {
            // force = true : recalcule tout (corrige d'anciennes valeurs) ;
            // sinon : ne traite que les offres sans salaire chiffré (rapide au démarrage).
            // Le filtre ne testait que MinSalary : une offre « jusqu'a X »,
            // dont seule la borne haute est connue, gardait donc un
            // MinSalary nul et se faisait reprendre a chaque demarrage —
            // reecrite indefiniment pour le meme resultat, et comptee comme
            // « mise a jour » dans le journal.
            var batch = await _context.JobOffers
                .Where(j => j.ExternalSource != null && j.Salary != null && j.Salary != ""
                    && (force || (j.MinSalary == null && j.MaxSalary == null)) && j.Id > lastId)
                .OrderBy(j => j.Id).Take(take).ToListAsync(ct);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;
            foreach (var j in batch)
            {
                var (min, max) = ParseFtSalary(j.Salary);
                // Une borne seule est un résultat valide : « à partir de X »
                // (plafond écarté) comme « jusqu'à X » (plancher non précisé).
                // Ne tester que `min` laissait en base l'ancienne valeur fausse
                // dans le second cas.
                if (min.HasValue || max.HasValue) { j.MinSalary = min; j.MaxSalary = max; updated++; }
                else if (force) { j.MinSalary = null; j.MaxSalary = null; } // efface une ancienne valeur erronée
            }
            await _context.SaveChangesAsync(ct);
            foreach (var j in batch) _context.Entry(j).State = EntityState.Detached;
            if (batch.Count < take) break;
        }
        _logger.LogInformation("Reparse salaires (force={Force}) : {Updated} offres mises à jour.", force, updated);
        return updated;
    }

    private static string MapCategory(string? c)
    {
        var s = (c ?? "").ToLowerInvariant();
        if (s.Contains("software") || s.Contains("dev") || s.Contains("engineer")) return "Tech";
        if (s.Contains("data")) return "Data";
        if (s.Contains("design") || s.Contains("product")) return "Design";
        if (s.Contains("market")) return "Marketing";
        if (s.Contains("financ") || s.Contains("account")) return "Finance";
        if (s.Contains("hr") || s.Contains("people")) return "RH";
        return "Autre";
    }

    private static string GuessCategory(string title, List<string?> tags)
    {
        var s = (title + " " + string.Join(" ", tags)).ToLowerInvariant();
        if (Regex.IsMatch(s, @"data|analyst|scientist|machine learning|\bml\b|\bbi\b")) return "Data";
        if (Regex.IsMatch(s, @"design|ux|ui|graphist|product designer")) return "Design";
        if (Regex.IsMatch(s, @"market|growth|seo|content|communication|community")) return "Marketing";
        if (Regex.IsMatch(s, @"financ|comptab|account|controleur|controller")) return "Finance";
        if (Regex.IsMatch(s, @"\brh\b|recrut|talent|human resource|people ops")) return "RH";
        if (Regex.IsMatch(s, @"dev|engineer|ingenieur|software|développeur|developpeur|fullstack|backend|frontend|devops|programmeur|tech")) return "Tech";
        return "Autre";
    }
}
