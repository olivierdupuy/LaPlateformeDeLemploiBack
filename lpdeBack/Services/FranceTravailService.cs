using System.Net;
using System.Text.Json;

namespace lpdeBack.Services;

/// <summary>
/// Accès aux API de francetravail.io.
///
/// Toutes ces API partagent le même serveur d'autorisation mais chacune
/// exige son propre scope, et un scope n'est délivré que si l'API a été
/// ajoutée à l'application sur francetravail.io. Un jeton par scope est
/// donc conservé séparément, et l'absence d'habilitation se distingue
/// d'une panne : la première se corrige d'un clic côté portail, la
/// seconde se réessaie.
///
/// Les jetons vivent une vingtaine de minutes. Les redemander à chaque
/// requête ferait un aller-retour supplémentaire par appel et finirait
/// par heurter les quotas du serveur d'autorisation.
/// </summary>
public class FranceTravailService
{
    private const string TokenUrl =
        "https://entreprise.francetravail.fr/connexion/oauth2/access_token?realm=%2Fpartenaire";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FranceTravailService> _logger;

    private readonly Dictionary<string, CachedToken> _tokens = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FranceTravailService(IHttpClientFactory httpFactory, IConfiguration config,
                                ILogger<FranceTravailService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    private sealed record CachedToken(string Value, DateTime ExpiresAt);

    /// <summary>Résultat d'un appel, avec la raison d'un éventuel échec.</summary>
    public sealed record Result(bool Ok, JsonDocument? Data, FtError Error, string? Message)
    {
        public static Result Success(JsonDocument d) => new(true, d, FtError.None, null);
        public static Result Fail(FtError e, string? m = null) => new(false, null, e, m);
    }

    public enum FtError
    {
        None,
        /// <summary>Aucun identifiant configuré côté serveur.</summary>
        NotConfigured,
        /// <summary>L'API n'est pas rattachée à l'application francetravail.io.</summary>
        NotSubscribed,
        /// <summary>Le service distant a répondu en erreur.</summary>
        Upstream,
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["FranceTravail:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_config["FranceTravail:ClientSecret"]);

    private async Task<(string? token, FtError error)> GetTokenAsync(string scope, CancellationToken ct)
    {
        if (!IsConfigured) return (null, FtError.NotConfigured);

        await _lock.WaitAsync(ct);
        try
        {
            if (_tokens.TryGetValue(scope, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
                return (cached.Value, FtError.None);

            var http = _httpFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _config["FranceTravail:ClientId"]!,
                    ["client_secret"] = _config["FranceTravail:ClientSecret"]!,
                    ["scope"] = scope,
                }),
            };

            var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                // « invalid_scope » ne veut pas dire que le scope n'existe
                // pas : il signifie le plus souvent que l'API n'a pas ete
                // ajoutee a l'application. C'est une action de portail, pas
                // un incident — on le distingue pour pouvoir le dire.
                var notSubscribed = body.Contains("invalid_scope", StringComparison.OrdinalIgnoreCase);
                _logger.LogWarning("France Travail : jeton refuse pour {Scope} ({Code})", scope, resp.StatusCode);
                return (null, notSubscribed ? FtError.NotSubscribed : FtError.Upstream);
            }

            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.GetProperty("access_token").GetString();
            var ttl = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 1500;

            // Marge d'une minute : un jeton qui expire pendant le vol
            // provoquerait un 401 que rien ne rattraperait.
            _tokens[scope] = new CachedToken(token!, DateTime.UtcNow.AddSeconds(ttl - 60));
            return (token, FtError.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "France Travail : echec de recuperation du jeton");
            return (null, FtError.Upstream);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<Result> GetAsync(string scope, string url, CancellationToken ct = default)
        => SendAsync(scope, HttpMethod.Get, url, null, ct);

    public Task<Result> PostAsync(string scope, string url, HttpContent body, CancellationToken ct = default)
        => SendAsync(scope, HttpMethod.Post, url, body, ct);

    /// <summary>
    /// Certaines API du catalogue repondent en XML par defaut — celle du
    /// marche du travail notamment. Elles savent produire du JSON, mais
    /// seulement si on le demande explicitement.
    /// </summary>
    public Task<Result> GetJsonAsync(string scope, string url, CancellationToken ct = default)
        => SendAsync(scope, HttpMethod.Get, url, null, ct, forceJson: true);

    public Task<Result> PostJsonAsync(string scope, string url, HttpContent body, CancellationToken ct = default)
        => SendAsync(scope, HttpMethod.Post, url, body, ct, forceJson: true);

    private async Task<Result> SendAsync(string scope, HttpMethod method, string url,
                                         HttpContent? body, CancellationToken ct,
                                         bool forceJson = false)
    {
        var (token, error) = await GetTokenAsync(scope, ct);
        if (token == null) return Result.Fail(error);

        try
        {
            var http = _httpFactory.CreateClient();
            var req = new HttpRequestMessage(method, url) { Content = body };
            req.Headers.Add("Authorization", "Bearer " + token);
            if (forceJson) req.Headers.Add("Accept", "application/json");

            var resp = await http.SendAsync(req, ct);

            // 204 : la recherche a abouti mais ne renvoie rien. Ce n'est pas
            // une erreur, et un appelant doit pouvoir l'afficher comme tel.
            if (resp.StatusCode == HttpStatusCode.NoContent)
                return Result.Success(JsonDocument.Parse("[]"));

            if (resp.StatusCode == HttpStatusCode.Forbidden)
                return Result.Fail(FtError.NotSubscribed);

            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("France Travail : {Code} sur {Url}", resp.StatusCode, url);
                return Result.Fail(FtError.Upstream, $"{(int)resp.StatusCode}");
            }

            return Result.Success(JsonDocument.Parse(text));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "France Travail : appel echoue sur {Url}", url);
            return Result.Fail(FtError.Upstream, ex.Message);
        }
    }
}
