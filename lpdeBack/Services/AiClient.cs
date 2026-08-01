using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace lpdeBack.Services;

/// <summary>
/// Reglages du modele de langage. Le contrat vise est celui de l'API
/// « chat/completions » d'OpenAI, que servent aussi Ollama, llama.cpp et
/// LM Studio : basculer d'un fournisseur a l'autre ne demande que de changer
/// ces valeurs, pas une ligne de code.
/// </summary>
public class AiOptions
{
    /// <summary>
    /// Racine de l'API. En dialecte « openai », terminaison « /v1 » comprise ;
    /// en dialecte « ollama », la racine du serveur, sans suffixe.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Dialecte parle par le serveur : « openai » ou « ollama ».
    ///
    /// Ollama sert les deux — son API native sur « /api/chat » et une couche de
    /// compatibilite OpenAI sur « /v1/chat/completions ». Une passerelle placee
    /// devant lui peut cependant n'en autoriser qu'une : celle du reseau local
    /// filtre les chemins « /v1/* » et renvoie 403. D'ou ce reglage, qui evite
    /// de dependre du bon vouloir d'un proxy qu'on n'administre pas.
    /// </summary>
    public string Api { get; set; } = "openai";

    public string Model { get; set; } = string.Empty;

    /// <summary>Jeton porteur (OpenAI et compatibles).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Authentification basique, si un proxy protege le serveur local.</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Un modele local repond en dizaines de secondes : le defaut .NET
    /// de 100 s ne suffit pas pour l'analyse d'un CV entier.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>A n'activer que pour un serveur du reseau local a certificat
    /// auto-signe : la validation TLS est alors desactivee pour cet appel.</summary>
    public bool AcceptInvalidCertificate { get; set; }
}

/// <summary>Resultat d'un appel au modele, transposable tel quel en reponse HTTP.</summary>
public record AiResult(bool Ok, string? Content, string? Error, int Status)
{
    public static AiResult Success(string content) => new(true, content, null, 200);
    public static AiResult Failure(string error, int status) => new(false, null, error, status);
}

/// <summary>
/// Client de generation de texte. Il concentre ce qui doit etre fait partout de
/// la meme facon : authentification, delai d'attente, mode JSON, nettoyage de la
/// reponse et traduction des pannes en messages comprehensibles.
/// </summary>
public class AiClient
{
    public const string HttpClientName = "ai";

    private readonly IHttpClientFactory _factory;
    private readonly AiOptions _options;
    private readonly ILogger<AiClient> _logger;

    public AiClient(IHttpClientFactory factory, IOptions<AiOptions> options, ILogger<AiClient> logger)
    {
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.BaseUrl) && !string.IsNullOrWhiteSpace(_options.Model);

    /// <summary>Le serveur parle-t-il l'API native d'Ollama plutot que celle d'OpenAI ?</summary>
    private bool EstOllama =>
        string.Equals(_options.Api, "ollama", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parle-t-on a Claude ?
    ///
    /// L'API d'Anthropic ne se glisse pas dans le moule d'OpenAI, et quatre
    /// details la distinguent — chacun suffisant a faire echouer l'appel :
    ///   - la consigne systeme est un parametre a part, pas un message de
    ///     role « system » ;
    ///   - l'authentification passe par « x-api-key », pas par
    ///     « Authorization: Bearer » ;
    ///   - « max_tokens » est obligatoire, l'omettre vaut un 400 ;
    ///   - la reponse arrive en blocs de contenu, pas en « choices ».
    /// </summary>
    private bool EstAnthropic =>
        string.Equals(_options.Api, "anthropic", StringComparison.OrdinalIgnoreCase);

    public string Model => _options.Model;

    public async Task<AiResult> ChatAsync(
        string systemPrompt,
        string userPrompt,
        double temperature = 0.2,
        int maxTokens = 4000,
        bool jsonMode = true,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return AiResult.Failure("Aucun modele de langage n'est configure (section « Ai » des reglages).", 503);

        var client = _factory.CreateClient(HttpClientName);

        if (EstAnthropic && !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            // Anthropic ignore « Authorization » et repond 401 : la cle se
            // presente dans son propre en-tete, accompagnee de la version
            // du contrat — sans laquelle l'API refuse aussi de repondre.
            client.DefaultRequestHeaders.Remove("x-api-key");
            client.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
            client.DefaultRequestHeaders.Remove("anthropic-version");
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
        else if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        else if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        var messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt },
        };

        var racine = _options.BaseUrl.TrimEnd('/');
        var body = new Dictionary<string, object?>();
        string url;

        if (EstAnthropic)
        {
            url = racine + "/messages";
            body["model"] = _options.Model;
            // La consigne systeme est un parametre, pas un message : passee
            // en message de role « system », Anthropic la refuse.
            body["system"] = jsonMode
                ? systemPrompt + "\n\nRepondez uniquement par du JSON valide, " +
                  "sans texte avant ni apres, et sans bloc de code."
                : systemPrompt;
            body["messages"] = new object[] { new { role = "user", content = userPrompt } };
            body["temperature"] = temperature;
            // Obligatoire ici, contrairement a OpenAI.
            body["max_tokens"] = maxTokens;
        }
        else if (EstOllama)
        {
            url = racine + "/api/chat";
            body["model"] = _options.Model;
            body["messages"] = messages;
            body["stream"] = false;
            // Ollama porte temperature et longueur maximale dans « options »,
            // pas a la racine du corps comme OpenAI.
            body["options"] = new { temperature, num_predict = maxTokens };

            // Sans ce reglage, les modeles ouverts encadrent volontiers leur
            // reponse d'un bloc ```json, que le desserialiseur refuse.
            if (jsonMode) body["format"] = "json";
        }
        else
        {
            url = racine + "/chat/completions";
            body["model"] = _options.Model;
            body["messages"] = messages;
            body["temperature"] = temperature;
            body["max_tokens"] = maxTokens;
            body["stream"] = false;

            if (jsonMode) body["response_format"] = new { type = "json_object" };
        }

        try
        {
            var response = await client.PostAsJsonAsync(url, body, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Modele {Model} : reponse {Status} — {Detail}",
                    _options.Model, (int)response.StatusCode, Truncate(detail, 500));

                var message = (int)response.StatusCode switch
                {
                    401 => "Le serveur du modele a refuse l'authentification.",
                    // Un 403 sur une passerelle vient plus souvent d'un chemin
                    // non autorise que d'un mot de passe faux : la passerelle du
                    // reseau local filtre « /v1/* ». Le reglage « Ai:Api » est
                    // alors le premier endroit ou regarder.
                    403 => $"La passerelle a refuse l'appel a « {url} » (chemin non autorise ou identifiants rejetes).",
                    404 => $"Modele « {_options.Model} » introuvable sur le serveur.",
                    _ => "Le serveur du modele a renvoye une erreur.",
                };
                return AiResult.Failure(message, 502);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            // Trois enveloppes pour la meme chose : OpenAI met la reponse dans
            // « choices[0].message », Ollama dans « message », Anthropic dans
            // un tableau de blocs dont on prend le premier bloc de texte.
            string? content;
            if (EstAnthropic)
            {
                content = null;
                if (doc.RootElement.TryGetProperty("content", out var blocs)
                    && blocs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var bloc in blocs.EnumerateArray())
                    {
                        if (bloc.TryGetProperty("type", out var t) && t.GetString() == "text"
                            && bloc.TryGetProperty("text", out var txt))
                        {
                            content = txt.GetString();
                            break;
                        }
                    }
                }
            }
            else if (EstOllama)
            {
                content = doc.RootElement.TryGetProperty("message", out var m)
                    && m.TryGetProperty("content", out var c) ? c.GetString() : null;
            }
            else
            {
                content = doc.RootElement.TryGetProperty("choices", out var choices)
                    && choices.GetArrayLength() > 0
                        ? choices[0].GetProperty("message").GetProperty("content").GetString()
                        : null;
            }

            if (content is null)
                return AiResult.Failure("Reponse inexploitable du modele.", 502);

            if (string.IsNullOrWhiteSpace(content))
                return AiResult.Failure("Le modele a renvoye une reponse vide.", 502);

            return AiResult.Success(jsonMode ? IsolerJson(StripCodeFences(content)) : StripCodeFences(content));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Modele {Model} : delai depasse ({Timeout} s).", _options.Model, _options.TimeoutSeconds);
            return AiResult.Failure(
                $"Le modele n'a pas repondu dans le delai imparti ({_options.TimeoutSeconds} s). Reessayez.", 504);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Modele {Model} : serveur injoignable a {Url}.", _options.Model, url);
            return AiResult.Failure("Le serveur du modele est injoignable.", 503);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Modele {Model} : reponse illisible.", _options.Model);
            return AiResult.Failure("Reponse illisible du modele.", 502);
        }
    }

    /// <summary>
    /// Ne garde que le JSON, quand c'est du JSON qu'on attend.
    ///
    /// OpenAI et Ollama ont un mode qui garantit une reponse JSON pure.
    /// Anthropic n'en a pas : on le demande dans la consigne, ce qui suffit
    /// presque toujours, mais « Voici le resultat : { … } » reste possible.
    /// Une phrase de politesse en tete faisait alors echouer la lecture de
    /// tout le document. On coupe donc a la premiere accolade ou au premier
    /// crochet, et a leur dernier repondant.
    ///
    /// Sans effet quand la reponse est deja propre : c'est un filet, pas un
    /// traitement.
    /// </summary>
    private static string IsolerJson(string contenu)
    {
        var t = contenu.Trim();
        if (t.Length == 0 || t[0] is '{' or '[') return t;

        var debut = t.IndexOfAny(new[] { '{', '[' });
        if (debut < 0) return t;

        var fin = t.LastIndexOfAny(new[] { '}', ']' });
        return fin > debut ? t[debut..(fin + 1)] : t[debut..];
    }

    /// <summary>Retire l'encadrement ```json … ``` que certains modeles ajoutent.</summary>
    private static string StripCodeFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;

        var firstBreak = trimmed.IndexOf('\n');
        if (firstBreak < 0) return trimmed;

        trimmed = trimmed[(firstBreak + 1)..];
        var closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return (closing >= 0 ? trimmed[..closing] : trimmed).Trim();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
