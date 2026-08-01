using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Xml;
using lpdeBack.Data;

namespace lpdeBack.Controllers;

/// <summary>
/// Plans de site.
///
/// Le catalogue compte plus de cent mille offres : aucun moteur ne les
/// trouvera en suivant des liens depuis l'accueil, d'autant que le site
/// est une application monopage — le robot ne voit qu'une coquille tant
/// qu'il n'execute pas le JavaScript. Le plan de site est donc le seul
/// canal fiable pour lui dire ce qui existe.
///
/// Il est genere ici et non ecrit a la main : un fichier statique serait
/// perime le lendemain de l'import suivant.
///
/// Les URL pointent vers le site public, pas vers l'API. Le plan est
/// declare dans le robots.txt de ce site, ce qui autorise Google et Bing
/// a l'accepter bien qu'il soit servi depuis un autre hote.
/// </summary>
[ApiController]
[Route("api/seo")]
[AllowAnonymous]
public class SeoController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public SeoController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    /// <summary>Hote public. Configurable, car il differe entre recette et production.</summary>
    private string Site => (_config["Seo:SiteUrl"] ?? "https://www.laplateformedelemploi.com").TrimEnd('/');

    /// <summary>Adresse de ce controleur, pour que l'index se pointe lui-meme.</summary>
    private string ApiBase => $"{Request.Scheme}://{Request.Host}/api/seo";

    /// <summary>
    /// La limite du protocole est de 50 000 URL par fichier. On reste en
    /// deca : un fichier plus petit se retelecharge plus souvent, et une
    /// offre nouvelle est vue plus vite.
    /// </summary>
    private const int ParFichier = 25_000;

    /// <summary>
    /// Une annonce sans description ne merite pas d'etre indexee : elle
    /// n'apporte rien a qui la trouverait, et une page maigre repetee des
    /// centaines de fois abime le jugement porte sur tout le domaine.
    /// </summary>
    private const int DescriptionMinimale = 120;

    private IQueryable<Models.JobOffer> Indexables() =>
        _context.JobOffers.Where(j =>
            j.IsActive
            && !j.IsDraft
            && j.ModerationStatus == "Approved"
            && (j.ExpiresAt == null || j.ExpiresAt > DateTime.UtcNow)
            && j.Description.Length >= DescriptionMinimale);

    // ═══════════════════════════════════════════
    //  Index : le seul fichier a declarer aux moteurs
    // ═══════════════════════════════════════════

    [HttpGet("sitemap.xml")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> SitemapIndex()
    {
        var offres = await Indexables().CountAsync();
        var pagesOffres = Math.Max(1, (int)Math.Ceiling(offres / (double)ParFichier));

        var entreprises = await Indexables().Select(j => j.Company).Distinct().CountAsync();
        var pagesEntreprises = Math.Max(1, (int)Math.Ceiling(entreprises / (double)ParFichier));

        var maj = await Indexables().MaxAsync(j => (DateTime?)j.CreatedAt) ?? DateTime.UtcNow;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        void Ajouter(string chemin, DateTime date)
        {
            sb.AppendLine("  <sitemap>");
            sb.AppendLine($"    <loc>{Echapper($"{ApiBase}/{chemin}")}</loc>");
            sb.AppendLine($"    <lastmod>{date:yyyy-MM-dd}</lastmod>");
            sb.AppendLine("  </sitemap>");
        }

        Ajouter("sitemaps/pages.xml", DateTime.UtcNow);
        for (var p = 1; p <= pagesOffres; p++) Ajouter($"sitemaps/offres-{p}.xml", maj);
        for (var p = 1; p <= pagesEntreprises; p++) Ajouter($"sitemaps/entreprises-{p}.xml", maj);
        Ajouter("sitemaps/metiers.xml", maj);

        sb.AppendLine("</sitemapindex>");
        return Xml(sb.ToString());
    }

    // ═══════════════════════════════════════════
    //  Pages fixes
    // ═══════════════════════════════════════════

    [HttpGet("sitemaps/pages.xml")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult PagesFixes()
    {
        // Priorite et frequence sont des indications, pas des ordres : les
        // moteurs les ignorent largement. Elles restent utiles a la
        // lecture humaine du fichier.
        var pages = new (string Chemin, string Freq, string Prio)[]
        {
            ("", "daily", "1.0"),
            ("offres", "hourly", "0.9"),
            ("parcourir", "daily", "0.8"),
            ("entreprises", "daily", "0.8"),
            ("salaires", "weekly", "0.8"),
            ("guide", "weekly", "0.7"),
            ("evenements", "weekly", "0.6"),
        };

        var sb = Entete();
        foreach (var (chemin, freq, prio) in pages)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{Echapper($"{Site}/{chemin}")}</loc>");
            sb.AppendLine($"    <changefreq>{freq}</changefreq>");
            sb.AppendLine($"    <priority>{prio}</priority>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        return Xml(sb.ToString());
    }

    // ═══════════════════════════════════════════
    //  Offres
    // ═══════════════════════════════════════════

    [HttpGet("sitemaps/offres-{page:int}.xml")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Offres(int page)
    {
        if (page < 1) return NotFound();

        var lot = await Indexables()
            .OrderBy(j => j.Id)
            .Skip((page - 1) * ParFichier)
            .Take(ParFichier)
            .Select(j => new { j.Id, j.CreatedAt })
            .ToListAsync();

        if (lot.Count == 0) return NotFound();

        var sb = Entete();
        foreach (var o in lot)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{Echapper($"{Site}/offres/{o.Id}")}</loc>");
            sb.AppendLine($"    <lastmod>{o.CreatedAt:yyyy-MM-dd}</lastmod>");
            sb.AppendLine("    <changefreq>weekly</changefreq>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        return Xml(sb.ToString());
    }

    // ═══════════════════════════════════════════
    //  Entreprises et metiers
    // ═══════════════════════════════════════════

    [HttpGet("sitemaps/entreprises-{page:int}.xml")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Entreprises(int page)
    {
        if (page < 1) return NotFound();

        // Les libelles generiques ne designent aucune organisation : leur
        // fiche n'aurait rien a montrer, et « Entreprise » repete des
        // milliers de fois est exactement ce qu'un moteur appelle du
        // contenu duplique.
        var generiques = new[] { "entreprise", "confidentiel", "recruteur", "employeur" };

        var lot = await Indexables()
            .Select(j => j.Company)
            .Where(c => c != null && c != "" && !generiques.Contains(c.ToLower()))
            .Distinct()
            .OrderBy(c => c)
            .Skip((page - 1) * ParFichier)
            .Take(ParFichier)
            .ToListAsync();

        if (lot.Count == 0) return NotFound();

        var sb = Entete();
        foreach (var c in lot)
        {
            // Le front construit ses liens avec le nom brut, encode par le
            // routeur. Le plan doit produire exactement la meme chaine,
            // sinon il annonce des pages qui repondront 404.
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{Echapper($"{Site}/entreprises/{Uri.EscapeDataString(c)}")}</loc>");
            sb.AppendLine("    <changefreq>weekly</changefreq>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        return Xml(sb.ToString());
    }

    [HttpGet("sitemaps/metiers.xml")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Metiers()
    {
        // Une moyenne calculee sur une seule offre n'est pas une reference
        // de salaire : la page existerait pour un chiffre qui ne veut rien
        // dire. On ne declare que les metiers assez representes.
        var metiers = await Indexables()
            .Where(j => j.MinSalary != null)
            .GroupBy(j => j.Title)
            .Where(g => g.Count() >= 3)
            .Select(g => g.Key)
            .OrderBy(t => t)
            .Take(ParFichier)
            .ToListAsync();

        var sb = Entete();
        foreach (var t in metiers)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{Echapper($"{Site}/salaires/metier/{Uri.EscapeDataString(t)}")}</loc>");
            sb.AppendLine("    <changefreq>monthly</changefreq>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        return Xml(sb.ToString());
    }

    // ═══════════════════════════════════════════
    //  Outils
    // ═══════════════════════════════════════════

    private static StringBuilder Entete()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        return sb;
    }

    /// <summary>
    /// Une esperluette ou un accent aigu non echappe rend le fichier
    /// entier invalide, et un plan invalide n'est pas lu du tout.
    /// </summary>
    private static string Echapper(string valeur) => new XmlDocument()
        .CreateTextNode(valeur).OuterXml;

    private IActionResult Xml(string contenu) =>
        Content(contenu, "application/xml", Encoding.UTF8);
}
