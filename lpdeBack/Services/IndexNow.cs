using System.Text;
using System.Text.Json;

namespace lpdeBack.Services;

/// <summary>
/// Prevenir les moteurs qu'une adresse a change.
///
/// Le plan de site annonce cent mille offres, et il est relu quand le
/// moteur le decide — au mieux une fois par jour, souvent moins. Une
/// offre publiee ce matin n'est donc indexee que le lendemain ou la
/// semaine suivante, alors qu'elle sera pourvue dans quinze jours. Pour
/// un catalogue qui se renouvelle a ce rythme, l'exploration passive
/// arrive systematiquement trop tard.
///
/// IndexNow renverse le sens : c'est nous qui signalons. Un seul appel
/// sert Bing, Yandex, Seznam et Naver — ils partagent la meme file.
/// Google n'y participe pas ; pour lui, le plan de site reste le canal.
///
/// ── Ce qu'il faut pour l'activer ──
/// Une cle (« Seo:IndexNowKey ») et un fichier du meme nom, contenant
/// cette cle, servi a la racine du site public : c'est ainsi que le
/// moteur verifie qu'on parle bien pour ce domaine. Sans cle, le
/// service ne fait rien et le dit — le plan de site continue de servir.
/// </summary>
public class IndexNow
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<IndexNow> _journal;

    /// <summary>
    /// Le protocole plafonne a 10 000 adresses par appel. On reste bien
    /// en deca : un import qui signalerait dix mille offres d'un coup
    /// serait de toute facon traite comme du bruit.
    /// </summary>
    private const int MaxParAppel = 1_000;

    public IndexNow(IConfiguration config, IHttpClientFactory clients, ILogger<IndexNow> journal)
    {
        _config = config;
        _clients = clients;
        _journal = journal;
    }

    private string? Cle => _config["Seo:IndexNowKey"];
    private string Site => (_config["Seo:SiteUrl"] ?? "https://www.laplateformedelemploi.com").TrimEnd('/');

    public bool EstConfigure => !string.IsNullOrWhiteSpace(Cle);

    /// <summary>De quoi rendre compte a l'administration, sans livrer la cle.</summary>
    public string Etat => EstConfigure
        ? "Cle presente : les nouvelles offres sont signalees a Bing et Yandex"
        : "Aucune cle IndexNow : l'indexation repose sur le plan de site seul";

    /// <summary>
    /// Signale des adresses. Ne leve jamais : prevenir un moteur est un
    /// confort, et rater ce confort ne doit pas faire echouer l'import
    /// qui l'a declenche.
    /// </summary>
    public async Task Signaler(IEnumerable<string> chemins, CancellationToken ct = default)
    {
        if (!EstConfigure) return;

        var adresses = chemins
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.StartsWith("http") ? c : $"{Site}/{c.TrimStart('/')}")
            .Distinct()
            .Take(MaxParAppel)
            .ToList();

        if (adresses.Count == 0) return;

        try
        {
            var hote = new Uri(Site).Host;
            var corps = JsonSerializer.Serialize(new
            {
                host = hote,
                key = Cle,
                // Le fichier de verification, servi a la racine du site
                // public. Sans lui, le moteur refuse le lot entier.
                keyLocation = $"{Site}/{Cle}.txt",
                urlList = adresses,
            });

            var client = _clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            var reponse = await client.PostAsync(
                "https://api.indexnow.org/indexnow",
                new StringContent(corps, Encoding.UTF8, "application/json"),
                ct);

            if (reponse.IsSuccessStatusCode)
            {
                _journal.LogInformation("IndexNow : {Nombre} adresses signalees", adresses.Count);
            }
            else
            {
                // 403 signifie presque toujours que le fichier de
                // verification manque ou ne contient pas la cle : c'est
                // la premiere chose a regarder.
                _journal.LogWarning(
                    "IndexNow a refuse le lot ({Code}). Verifier que {Fichier} existe et contient la cle.",
                    (int)reponse.StatusCode, $"{Site}/{Cle}.txt");
            }
        }
        catch (Exception ex)
        {
            _journal.LogWarning(ex, "IndexNow injoignable");
        }
    }

    /// <summary>Raccourci : signaler des offres par leur identifiant.</summary>
    public Task SignalerOffres(IEnumerable<int> identifiants, CancellationToken ct = default) =>
        Signaler(identifiants.Select(id => $"/offres/{id}"), ct);
}
