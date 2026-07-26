using System.Net.Http.Headers;
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

        if (toAdd.Count == 0) return 0;
        _context.JobOffers.AddRange(toAdd);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("Import: {Count} offres ajoutées.", toAdd.Count);
        return toAdd.Count;
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
                string.Join(", ", tags), null, null));
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
                true, string.Join(", ", tags), NullIfEmpty(Str(e, "salary")), null));
        }
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

        // 2) Recherche d'offres
        var searchReq = new HttpRequestMessage(HttpMethod.Get,
            "https://api.francetravail.io/partenaire/offresdemploi/v2/offres/search?range=0-149");
        searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var searchResp = await http.SendAsync(searchReq, ct);
        if (!searchResp.IsSuccessStatusCode) { _logger.LogWarning("France Travail: recherche échouée ({Code})", searchResp.StatusCode); return new(); }

        using var doc = JsonDocument.Parse(await searchResp.Content.ReadAsStringAsync(ct));
        var list = new List<JobOffer>();
        if (!doc.RootElement.TryGetProperty("resultats", out var results)) return list;

        foreach (var e in results.EnumerateArray())
        {
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

            var offer = BuildOffer(ext, "francetravail", title, company, location, StripHtml(Str(e, "description")),
                contract, category, false, null, salary, null);
            offer.Latitude = lat; offer.Longitude = lng;
            list.Add(offer);
        }
        return list;
    }

    // ── Construction & helpers ──
    private JobOffer BuildOffer(string ext, string source, string title, string company, string location,
        string description, string contract, string category, bool remote, string? tags, string? salary, string? _)
    {
        var geo = string.IsNullOrEmpty(location) ? null : GeoUtils.Geocode(location);
        return new JobOffer
        {
            ExternalId = ext,
            ExternalSource = source,
            Title = Trunc(title, 200),
            Company = Trunc(company, 100),
            Location = Trunc(location, 100),
            Description = string.IsNullOrWhiteSpace(description) ? "Voir le détail de l'offre." : description,
            ContractType = contract,
            Category = Trunc(category, 100),
            IsRemote = remote,
            Salary = salary != null ? Trunc(salary, 50) : null,
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
