using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Relais public vers les API de francetravail.io.
///
/// Le navigateur n'appelle jamais France Travail directement : les
/// identifiants resteraient exposes, et l'API n'autorise pas les appels
/// depuis un navigateur. Le serveur relaie donc, en ne renvoyant que ce
/// que les pages affichent.
///
/// Chaque route indique son etat d'habilitation plutot que de renvoyer un
/// tableau vide : une section qui ne montre rien parce qu'une API n'a pas
/// ete activee doit pouvoir le dire, sinon elle passe pour cassee.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FranceTravailController : ControllerBase
{
    // Chaque API demande DEUX scopes : celui de l'API et celui du domaine
    // fonctionnel. Le serveur d'autorisation delivre pourtant un jeton
    // avec le seul premier — c'est la passerelle qui refuse ensuite, en
    // 403. Un jeton obtenu ne prouve donc pas l'acces.
    private const string ScopeEvenements = "api_evenementsv1 evenements";
    private const string ScopeFichesMetiers = "api_rome-fiches-metiersv1 nomenclatureRome";
    private const string ScopeMarcheTravail = "api_stats-offres-demandes-emploiv1 offresetdemandesemploi";

    private const string ScopeBonneBoite = "api_labonneboitev2 search office";
    private const string ScopeRomeo = "api_romeov2 nomenclatureRome";

    private const string BaseEvenements = "https://api.francetravail.io/partenaire/evenements/v1";
    private const string BaseBonneBoite = "https://api.francetravail.io/partenaire/labonneboite/v2";
    private const string BaseRomeo = "https://api.francetravail.io/partenaire/romeo/v2";
    private const string BaseFiches = "https://api.francetravail.io/partenaire/rome-fiches-metiers/v1/fiches-rome";
    private const string BaseMarche = "https://api.francetravail.io/partenaire/stats-offres-demandes-emploi/v1";

    private readonly FranceTravailService _ft;

    public FranceTravailController(FranceTravailService ft) => _ft = ft;

    private IActionResult Render(FranceTravailService.Result r, string libelle)
    {
        if (r.Ok) return Content(r.Data!.RootElement.GetRawText(), "application/json");

        return r.Error switch
        {
            FranceTravailService.FtError.NotConfigured => StatusCode(503, new
            {
                etat = "non_configure",
                message = "Les identifiants France Travail ne sont pas renseignes sur le serveur.",
            }),
            FranceTravailService.FtError.NotSubscribed => StatusCode(503, new
            {
                etat = "non_habilite",
                message = $"L'API « {libelle} » n'est pas encore rattachee a l'application France Travail. "
                        + "Elle est en acces libre : il suffit de l'ajouter depuis francetravail.io.",
            }),
            _ => StatusCode(502, new
            {
                etat = "indisponible",
                message = "France Travail n'a pas repondu. Reessayez dans un instant.",
            }),
        };
    }

    /// <summary>
    /// Evenements emploi : forums, job datings, ateliers, salons en ligne.
    ///
    /// La recherche est un POST — les criteres passent dans le corps — et
    /// renvoie { totalElements, content }. On expose des parametres de
    /// requete simples parce que la page publique, elle, doit pouvoir se
    /// partager par son URL.
    /// </summary>
    [HttpGet("evenements")]
    public async Task<IActionResult> GetEvenements([FromQuery] string? departement,
                                                   [FromQuery] string? codePostal,
                                                   [FromQuery] int? type,
                                                   [FromQuery] string? modalite,
                                                   [FromQuery] string? secteur,
                                                   [FromQuery] string? dateDebut,
                                                   [FromQuery] string? dateFin,
                                                   CancellationToken ct)
    {
        var criteres = new Dictionary<string, object>();

        // Les departements et codes postaux se passent en tableaux, meme
        // pour une seule valeur.
        if (!string.IsNullOrWhiteSpace(departement)) criteres["departements"] = new[] { departement };
        if (!string.IsNullOrWhiteSpace(codePostal)) criteres["codePostal"] = new[] { codePostal };
        if (type.HasValue) criteres["typeEvenement"] = type.Value;
        if (!string.IsNullOrWhiteSpace(modalite)) criteres["modalite"] = modalite;
        if (!string.IsNullOrWhiteSpace(secteur)) criteres["secteurActivite"] = secteur;

        // Sans borne basse, l'API remonte aussi les evenements passes : une
        // page qui invite a s'inscrire ne doit montrer que l'a-venir.
        criteres["dateDebut"] = string.IsNullOrWhiteSpace(dateDebut)
            ? DateTime.Today.ToString("yyyy-MM-dd")
            : dateDebut;
        if (!string.IsNullOrWhiteSpace(dateFin)) criteres["dateFin"] = dateFin;

        var body = new StringContent(JsonSerializer.Serialize(criteres), Encoding.UTF8, "application/json");
        var r = await _ft.PostAsync(ScopeEvenements, $"{BaseEvenements}/mee/evenements", body, ct);
        return Render(r, "Mes evenements emploi");
    }

    /// <summary>
    /// Detail d'un evenement. La contrainte de type est necessaire : sans
    /// elle, « /evenements/operations » entrerait ici et echouerait.
    /// </summary>
    [HttpGet("evenements/{id:long}")]
    public async Task<IActionResult> GetEvenement(long id, CancellationToken ct)
    {
        var r = await _ft.GetAsync(ScopeEvenements, $"{BaseEvenements}/mee/evenement/{id}", ct);
        return Render(r, "Mes evenements emploi");
    }

    /// <summary>Referentiel des operations nationales (#1jeune1solution…).</summary>
    [HttpGet("evenements/operations")]
    public async Task<IActionResult> GetOperations(CancellationToken ct)
    {
        var r = await _ft.GetAsync(ScopeEvenements, $"{BaseEvenements}/mee/tags/opera/lister", ct);
        return Render(r, "Mes evenements emploi");
    }

    /// <summary>
    /// Traduit un intitule libre en codes metier ROME.
    ///
    /// Le profil d'un candidat porte un intitule ecrit a la main
    /// (« developpeur web », « etudiant en informatique »). Les API
    /// metier, elles, raisonnent en codes ROME. ROMEO fait le pont, avec
    /// un score de confiance que l'on peut filtrer.
    /// </summary>
    [HttpGet("metiers/deviner")]
    public async Task<IActionResult> DevinerMetier([FromQuery] string intitule, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(intitule))
            return BadRequest(new { message = "Intitule manquant." });

        var payload = new
        {
            appellations = new[] { new { intitule, identifiant = "1" } },
            options = new { nomAppelant = "lpde", nbResultats = 5, seuilScorePrediction = 0.4 },
        };

        var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var r = await _ft.PostAsync(ScopeRomeo, $"{BaseRomeo}/predictionMetiers", body, ct);
        return Render(r, "ROMEO");
    }

    /// <summary>
    /// Entreprises a fort potentiel d'embauche (La Bonne Boite).
    ///
    /// Ces entreprises n'ont pas forcement publie d'offre : le classement
    /// vient de leur historique de recrutement. C'est la matiere d'une
    /// candidature spontanee, que ne donne aucun agregateur d'annonces.
    /// </summary>
    [HttpGet("entreprises-qui-recrutent")]
    public async Task<IActionResult> GetEntreprises([FromQuery] string? rome,
                                                    [FromQuery] string? metier,
                                                    [FromQuery] string? ville,
                                                    [FromQuery] string? departement,
                                                    [FromQuery] int distance = 30,
                                                    [FromQuery] int page = 1,
                                                    [FromQuery] int taille = 20,
                                                    CancellationToken ct = default)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(rome)) q.Add($"rome={Uri.EscapeDataString(rome)}");
        else if (!string.IsNullOrWhiteSpace(metier)) q.Add($"job={Uri.EscapeDataString(metier)}");

        if (!string.IsNullOrWhiteSpace(ville)) q.Add($"city={Uri.EscapeDataString(ville)}");
        else if (!string.IsNullOrWhiteSpace(departement)) q.Add($"department_number={Uri.EscapeDataString(departement)}");

        if (q.Count == 0)
            return BadRequest(new { message = "Precisez au moins un metier ou un code ROME." });

        q.Add($"distance={Math.Clamp(distance, 5, 100)}");
        q.Add($"page={Math.Max(1, page)}");
        q.Add($"page_size={Math.Clamp(taille, 1, 50)}");

        var r = await _ft.GetAsync(ScopeBonneBoite, $"{BaseBonneBoite}/recherche?{string.Join("&", q)}", ct);
        return Render(r, "La Bonne Boite");
    }

    /// <summary>Liste des 532 metiers ROME, pour la recherche.</summary>
    [HttpGet("metiers")]
    public async Task<IActionResult> ListerMetiers(CancellationToken ct)
    {
        var r = await _ft.GetAsync(ScopeFichesMetiers, $"{BaseFiches}/fiche-metier", ct);
        return Render(r, "ROME 4.0 - Fiches metiers");
    }

    /// <summary>
    /// Fiche metier ROME 4.0 : competences mobilisees et savoirs.
    ///
    /// Un code ROME fait exactement cinq caracteres : la contrainte de
    /// longueur suffit a ecarter « /metiers/deviner », sans recourir a une
    /// expression reguliere — les crochets y seraient pris pour des jetons
    /// de route et empecheraient l'application de demarrer.
    /// </summary>
    [HttpGet("metiers/{code:length(5)}")]
    public async Task<IActionResult> GetFicheMetier(string code, CancellationToken ct)
    {
        var r = await _ft.GetAsync(ScopeFichesMetiers, $"{BaseFiches}/fiche-metier/{code}", ct);
        return Render(r, "ROME 4.0 - Fiches metiers");
    }

    /// <summary>
    /// Marche du travail sur un metier et un territoire.
    ///
    /// Les combinaisons de codes ne sont pas libres : chaque indicateur
    /// declare les types de territoire, d'activite, de periode et de
    /// nomenclature qu'il accepte. OFF_1 veut ORIGINEOFF et TRIMESTRE ;
    /// tout autre couple est rejete en 400.
    /// </summary>
    [HttpGet("marche-du-travail")]
    public async Task<IActionResult> GetMarcheDuTravail([FromQuery] string rome,
                                                        [FromQuery] string? departement,
                                                        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rome))
            return BadRequest(new { message = "Code ROME manquant." });

        // Sans departement, on regarde la France entiere : un metier se
        // compare mieux a son marche national qu'a rien du tout.
        var national = string.IsNullOrWhiteSpace(departement);

        var criteres = new Dictionary<string, object>
        {
            ["codeTypeTerritoire"] = national ? "NAT" : "DEP",
            ["codeTerritoire"] = national ? "1" : departement!,
            ["codeTypeActivite"] = "ROME",
            ["codeActivite"] = rome,
            ["codeTypePeriode"] = "TRIMESTRE",
            ["codeTypeNomenclature"] = "ORIGINEOFF",
            ["dernierePeriode"] = true,
            ["sansCaracteristiques"] = true,
        };

        var body = new StringContent(JsonSerializer.Serialize(criteres), Encoding.UTF8, "application/json");
        var r = await _ft.PostJsonAsync(ScopeMarcheTravail, $"{BaseMarche}/indicateur/stat-offres", body, ct);
        return Render(r, "Marche du travail");
    }

    /// <summary>
    /// Etat des habilitations, pour que l'interface sache quoi proposer
    /// sans avoir a essuyer un echec par section.
    /// </summary>
    [HttpGet("etat")]
    public async Task<IActionResult> GetEtat(CancellationToken ct)
    {
        if (!_ft.IsConfigured)
            return Ok(new { configure = false, apis = Array.Empty<object>() });

        // On sollicite un vrai endpoint de chaque API : obtenir un jeton ne
        // prouve rien, c'est la passerelle qui tranche.
        async Task<object> Check(string cle, string libelle, string scope, string url)
        {
            var r = await _ft.GetAsync(scope, url, ct);
            return new { cle, libelle, habilite = r.Ok };
        }

        var apis = new[]
        {
            await Check("evenements", "Mes evenements emploi", ScopeEvenements,
                        $"{BaseEvenements}/mee/tags/opera/lister"),
        };

        return Ok(new { configure = true, apis });
    }
}
