using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
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

    public JobImportService(AppDbContext context, IHttpClientFactory httpFactory, IConfiguration config, ILogger<JobImportService> logger)
    {
        _context = context;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
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

    public async Task<int> ImportAllAsync(CancellationToken ct = default)
    {
        var existing = await _context.JobOffers
            .Where(j => j.ExternalId != null)
            .Select(j => j.ExternalId!)
            .ToListAsync(ct);
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var toAdd = new List<JobOffer>();
        try { toAdd.AddRange(await FetchArbeitnowAsync(seen, ct)); } catch (Exception e) { _logger.LogWarning(e, "Import Arbeitnow échoué"); }
        try { toAdd.AddRange(await FetchRemotiveAsync(seen, ct)); } catch (Exception e) { _logger.LogWarning(e, "Import Remotive échoué"); }
        try { toAdd.AddRange(await FetchFranceTravailAsync(seen, ct)); } catch (Exception e) { _logger.LogWarning(e, "Import France Travail échoué"); }
        try { toAdd.AddRange(await FetchAdzunaAsync(seen, ct)); } catch (Exception e) { _logger.LogWarning(e, "Import Adzuna échoué"); }
        try { toAdd.AddRange(await FetchJoobleAsync(seen, ct)); } catch (Exception e) { _logger.LogWarning(e, "Import Jooble échoué"); }

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
    public static (int? Min, int? Max) ParseFtSalary(string? libelle)
    {
        if (string.IsNullOrWhiteSpace(libelle)) return (null, null);
        var l = libelle.ToLowerInvariant();

        // Montants suivis de "euro(s)" ou "€" (ignore "12 mois", "35 h", etc.).
        var amounts = Regex.Matches(l, @"(\d+(?:[.,]\d+)?)\s*(?:euros?|€)")
            .Select(mt => ParseNum(mt.Groups[1].Value))
            .Where(d => d is > 0).Select(d => d!.Value)
            .ToList();
        if (amounts.Count == 0) return (null, null);

        // Nombre de mois de versement (13e/14e mois éventuel), défaut 12.
        double months = 12;
        var mm = Regex.Match(l, @"sur\s+(\d+(?:[.,]\d+)?)\s*mois");
        if (mm.Success && ParseNum(mm.Groups[1].Value) is double mv && mv is >= 12 and <= 16) months = mv;

        // Selon la période, seuls les montants dans une plage plausible sont retenus :
        // certains libellés FT « horaire » contiennent un montant annexe aberrant.
        // Plages plausibles (les données FT contiennent parfois des montants aberrants).
        double factor, lo, hi;
        if (l.Contains("annuel")) { factor = 1; lo = 8_000; hi = 350_000; }
        else if (l.Contains("mensuel")) { factor = months; lo = 400; hi = 30_000; }
        else if (l.Contains("horaire")) { factor = 35 * 52; lo = 3; hi = 200; } // 35 h/sem légales
        else
        {
            // Période absente : on déduit de l'ordre de grandeur du plus grand montant.
            var big = amounts.Max();
            if (big >= 10_000) { factor = 1; lo = 8_000; hi = 350_000; }
            else if (big >= 500) { factor = months; lo = 400; hi = 30_000; }
            else { factor = 35 * 52; lo = 3; hi = 200; }
        }

        var kept = amounts.Where(a => a >= lo && a <= hi).ToList();
        if (kept.Count == 0) return (null, null);

        int aMin = (int)Math.Round(kept.Min() * factor);
        int aMax = (int)Math.Round(kept.Max() * factor);
        if (aMax < 1000 || aMin > 1_000_000) return (null, null); // garde-fou plausibilité
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
        if (hi < 1000 || lo > 1_000_000) return (null, null); // garde-fou plausibilité
        return (aMin, aMax);
    }

    // Rétro-remplit le salaire chiffré des offres importées déjà en base (parcours par curseur d'Id).
    public async Task<int> ReparseSalariesAsync(bool force = false, CancellationToken ct = default)
    {
        int updated = 0, lastId = 0;
        const int take = 1000;
        while (!ct.IsCancellationRequested)
        {
            // force = true : recalcule tout (corrige d'anciennes valeurs) ;
            // sinon : ne traite que les offres sans salaire chiffré (rapide au démarrage).
            var batch = await _context.JobOffers
                .Where(j => j.ExternalSource != null && j.Salary != null && j.Salary != ""
                    && (force || j.MinSalary == null) && j.Id > lastId)
                .OrderBy(j => j.Id).Take(take).ToListAsync(ct);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;
            foreach (var j in batch)
            {
                var (min, max) = ParseFtSalary(j.Salary);
                if (min.HasValue) { j.MinSalary = min; j.MaxSalary = max; updated++; }
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
