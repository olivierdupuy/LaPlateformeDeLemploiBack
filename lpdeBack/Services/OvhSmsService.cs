using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace lpdeBack.Services;

/// <summary>
/// L'envoi de SMS par l'API d'OVH.
///
/// OVH ne signe pas ses appels avec un simple jeton : chaque requete porte
/// une empreinte SHA-1 du secret d'application, de la cle de consommateur,
/// de la methode, de l'URL entiere, du corps et de l'horodatage. Une
/// signature ne vaut donc que pour cette requete-la, a cette seconde-la —
/// interceptee, elle ne rejoue rien.
///
/// D'ou la seule subtilite de ce client : l'heure. OVH refuse une requete
/// dont l'horodatage s'ecarte de plus de trente secondes du sien, et une
/// machine dont l'horloge derive verrait tous ses envois refuses avec un
/// message qui ne parle pas d'heure. On lui demande donc la sienne une
/// fois, et on garde l'ecart.
/// </summary>
public class OvhSmsService
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<OvhSmsService> _log;
    private readonly IConfiguration _config;

    private readonly string? _ak, _as, _ck, _expediteur;
    private readonly string _racine;

    /// <summary>Ecart entre notre horloge et celle d'OVH, mesure une fois.</summary>
    private long? _decalage;

    /// <summary>Le compte SMS, decouvert au premier envoi si non configure.</summary>
    private string? _service;

    public OvhSmsService(IHttpClientFactory http, ILogger<OvhSmsService> log, IConfiguration config)
    {
        _http = http;
        _log = log;
        _config = config;

        _ak = config["Ovh:ApplicationKey"];
        _as = config["Ovh:ApplicationSecret"];
        _ck = config["Ovh:ConsumerKey"];
        _service = config["Ovh:SmsService"];
        _expediteur = config["Ovh:Sender"];
        _racine = (config["Ovh:Endpoint"] ?? "https://eu.api.ovh.com/1.0").TrimEnd('/');
    }

    public bool EstConfigure =>
        !string.IsNullOrWhiteSpace(_ak) && !string.IsNullOrWhiteSpace(_as) && !string.IsNullOrWhiteSpace(_ck);

    /// <summary>De quoi diagnostiquer sans rien divulguer.</summary>
    public string Etat => EstConfigure
        ? $"{_racine}, compte {_service ?? "(à découvrir)"}, expéditeur {_expediteur ?? "(numéro court OVH)"}"
        : "aucun identifiant OVH configuré : les SMS ne partent pas";

    // ══════════════════════════════════════
    //  Envoi
    // ══════════════════════════════════════

    /// <summary>
    /// Expedie un message. Rend vrai si OVH l'a accepte.
    ///
    /// Un echec ne leve pas : il est journalise et rendu. L'appelant doit
    /// pouvoir dire « le SMS n'est pas parti » a l'interesse plutot que de
    /// lui servir une erreur cinq cents.
    /// </summary>
    public async Task<(bool Parti, string? Erreur)> Envoyer(string destinataire, string message, CancellationToken ct = default)
    {
        if (!EstConfigure)
        {
            // Sans identifiants, le message va au journal — comme pour le
            // courriel. On suit un parcours complet en developpement sans
            // depenser un credit ni posseder de compte OVH.
            _log.LogWarning("SMS non expedie (aucun identifiant OVH).\n  A : {Destinataire}\n  {Message}",
                            destinataire, message);
            return (false, "Aucun identifiant OVH n'est configuré.");
        }

        var numero = Normaliser(destinataire);
        if (numero == null)
            return (false, "Numéro de téléphone illisible.");

        try
        {
            var service = await Service(ct);
            if (service == null)
                return (false, "Aucun compte SMS n'est rattaché à ces identifiants OVH.");

            // « noStopClause » n'est admis qu'avec un expediteur declare et
            // valide chez OVH. Sans expediteur, OVH impose sa mention STOP
            // et un numero court : le message passe quand meme, il est
            // seulement plus long.
            var corps = new Dictionary<string, object>
            {
                ["message"] = message,
                ["receivers"] = new[] { numero },
                ["charset"] = "UTF-8",
                ["class"] = "sms",
                ["priority"] = "high",
                ["noStopClause"] = !string.IsNullOrWhiteSpace(_expediteur),
            };
            if (!string.IsNullOrWhiteSpace(_expediteur)) corps["sender"] = _expediteur;
            else corps["senderForResponse"] = true;

            var reponse = await Appeler(HttpMethod.Post, $"/sms/{service}/jobs", corps, ct);
            if (!reponse.IsSuccessStatusCode)
            {
                var detail = await reponse.Content.ReadAsStringAsync(ct);
                _log.LogError("OVH a refuse l'envoi a {Numero} : {Code} {Detail}",
                              numero, (int)reponse.StatusCode, detail);
                return (false, LireErreur(detail, (int)reponse.StatusCode));
            }

            // OVH rend « invalidReceivers » : un numero syntaxiquement
            // correct mais inconnu de son operateur n'echoue pas la requete,
            // il ressort dans cette liste. Sans la lire, on annoncerait un
            // envoi reussi pour un message qui n'ira nulle part.
            using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("invalidReceivers", out var invalides)
                && invalides.ValueKind == JsonValueKind.Array && invalides.GetArrayLength() > 0)
            {
                _log.LogWarning("OVH a rejete le destinataire {Numero}", numero);
                return (false, "Ce numéro a été refusé par l'opérateur.");
            }

            var restants = doc.RootElement.TryGetProperty("totalCreditsRemoved", out var c) ? c.ToString() : "?";
            _log.LogInformation("SMS expedie a {Numero} ({Credits} credit(s))", Masquer(numero), restants);
            return (true, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Echec de l'envoi du SMS a {Numero}", Masquer(numero));
            return (false, "L'envoi a échoué. Réessayez dans un instant.");
        }
    }

    // ══════════════════════════════════════
    //  Appel signe
    // ══════════════════════════════════════

    private async Task<HttpResponseMessage> Appeler(HttpMethod methode, string chemin, object? corps, CancellationToken ct)
    {
        var url = _racine + chemin;
        var json = corps == null ? "" : JsonSerializer.Serialize(corps);
        var horodatage = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + await Decalage(ct)).ToString();

        // L'ordre et les signes « + » ne sont pas negociables : OVH
        // reconstruit la meme chaine de son cote et compare.
        var aSigner = $"{_as}+{_ck}+{methode.Method}+{url}+{json}+{horodatage}";
        var signature = "$1$" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(aSigner))).ToLowerInvariant();

        var requete = new HttpRequestMessage(methode, url);
        requete.Headers.Add("X-Ovh-Application", _ak);
        requete.Headers.Add("X-Ovh-Consumer", _ck);
        requete.Headers.Add("X-Ovh-Timestamp", horodatage);
        requete.Headers.Add("X-Ovh-Signature", signature);
        if (corps != null)
            requete.Content = new StringContent(json, Encoding.UTF8, "application/json");
        requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        return await client.SendAsync(requete, ct);
    }

    /// <summary>
    /// L'ecart avec l'horloge d'OVH, mesure une fois pour toutes.
    ///
    /// OVH refuse une requete dont l'horodatage s'ecarte de plus de trente
    /// secondes du sien. Une machine dont l'horloge derive verrait tous ses
    /// envois refuses par un « invalid signature » qui ne parle pas d'heure
    /// — la plus mauvaise piste possible.
    /// </summary>
    private async Task<long> Decalage(CancellationToken ct)
    {
        if (_decalage.HasValue) return _decalage.Value;
        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var texte = await client.GetStringAsync($"{_racine}/auth/time", ct);
            _decalage = long.Parse(texte.Trim()) - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(_decalage.Value) > 5)
                _log.LogWarning("L'horloge locale s'ecarte de {Ecart} s de celle d'OVH ; l'ecart est compense.", _decalage);
        }
        catch
        {
            _decalage = 0;
        }
        return _decalage.Value;
    }

    /// <summary>
    /// Le compte SMS. Il porte un nom du genre « sms-ab12345-1 » qu'aucun
    /// humain ne retient : s'il n'est pas configure, on le demande a OVH.
    /// </summary>
    private async Task<string?> Service(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_service)) return _service;
        try
        {
            var reponse = await Appeler(HttpMethod.Get, "/sms", null, ct);
            if (!reponse.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;
            _service = doc.RootElement[0].GetString();
            _log.LogInformation("Compte SMS OVH decouvert : {Service}", _service);
            return _service;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "La liste des comptes SMS n'a pas pu etre lue.");
            return null;
        }
    }

    // ══════════════════════════════════════
    //  Numeros
    // ══════════════════════════════════════

    /// <summary>
    /// OVH veut du format international. Les gens ecrivent « 06 12 34 56 78 »,
    /// « 06.12.34.56.78 » ou « +33 6 12 34 56 78 » : les trois doivent aboutir
    /// au meme numero, sans quoi le second facteur echouerait sur une
    /// question de ponctuation.
    /// </summary>
    public static string? Normaliser(string? saisie)
    {
        if (string.IsNullOrWhiteSpace(saisie)) return null;

        var n = new string(saisie.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (n.StartsWith("00")) n = "+" + n[2..];

        // Numero francais donne en national : 06… devient +336…
        if (n.Length == 10 && n[0] == '0') n = "+33" + n[1..];

        if (!n.StartsWith('+')) n = "+" + n;
        // Un indicatif plus un numero national tiennent entre 8 et 15 chiffres.
        var chiffres = n.Count(char.IsDigit);
        return chiffres is >= 8 and <= 15 ? n : null;
    }

    /// <summary>« +33 6 •• •• •• 78 » : de quoi se reconnaitre sans exposer le numero.</summary>
    public static string Masquer(string? numero)
    {
        var n = Normaliser(numero);
        if (n == null) return "numero inconnu";
        return n.Length <= 6 ? n : $"{n[..Math.Min(5, n.Length)]} •• •• •• {n[^2..]}";
    }

    private static string LireErreur(string corps, int code)
    {
        try
        {
            using var doc = JsonDocument.Parse(corps);
            if (doc.RootElement.TryGetProperty("message", out var m))
            {
                var texte = m.GetString() ?? "";
                // Le manque de credit est la panne la plus probable et la
                // seule que l'administration puisse corriger elle-meme.
                if (texte.Contains("credit", StringComparison.OrdinalIgnoreCase))
                    return "Le compte SMS OVH n'a plus de crédit.";
                return texte;
            }
        }
        catch { /* corps illisible : on rend le code */ }
        return $"OVH a répondu {code}.";
    }
}
