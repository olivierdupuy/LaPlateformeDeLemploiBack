using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Les pages « emploi &lt;metier&gt; a &lt;ville&gt; ».
///
/// C'est la requete que les gens tapent, et c'est la page qui manquait.
/// Le catalogue etait consultable par filtres, mais derriere des
/// parametres de requete que le robots.txt exclut lui-meme de
/// l'exploration — a juste titre : les combinaisons se comptent par
/// milliers. Aucune de ces vues n'avait donc d'adresse propre, de titre
/// propre, ni la moindre chance d'apparaitre dans un resultat de
/// recherche.
///
/// Ce controleur sert un jeu **fini** de combinaisons, et c'est la
/// difference : seules celles qui portent assez d'offres pour qu'une
/// page ait du contenu. Une page « emploi soudeur a Gueret » avec zero
/// offre est une page vide de plus, et cent mille pages vides abiment
/// le jugement porte sur tout le domaine.
/// </summary>
[ApiController]
[Route("api/emploi")]
[AllowAnonymous]
public class AtterrissageController : ControllerBase
{
    private readonly AppDbContext _context;

    public AtterrissageController(AppDbContext context) => _context = context;

    /// <summary>
    /// En deca, la page n'a pas de quoi exister. Trois offres, c'est
    /// peu — mais c'est deja trois raisons de rester, et cela suffit a
    /// ce que la page ne soit pas vide.
    /// </summary>
    public const int OffresMinimum = 3;

    private IQueryable<Models.JobOffer> Visibles() =>
        _context.JobOffers.AsNoTracking().Where(o =>
            o.IsActive && !o.IsDraft && o.ModerationStatus == "Approved");

    /// <summary>
    /// Les combinaisons qui meritent une page.
    ///
    /// Sert au plan de site et aux liens internes. Le calcul se fait en
    /// memoire apres un regroupement en base : les fragments d'adresse
    /// rassemblent des libelles que SQL considere distincts
    /// (« Paris 15e » et « 75 - Paris »), et aucun moteur SQL ne sait
    /// faire cette normalisation.
    /// </summary>
    [HttpGet("combinaisons")]
    [OutputCache(PolicyName = "reference")]
    public async Task<IActionResult> Combinaisons([FromQuery] int limite = 500)
    {
        var brut = await Visibles()
            .Where(o => o.Category != null && o.Category != "" && o.Location != null && o.Location != "")
            .GroupBy(o => new { o.Category, o.Location })
            .Select(g => new { g.Key.Category, g.Key.Location, Nombre = g.Count() })
            .ToListAsync();

        var groupees = brut
            .GroupBy(x => new { M = Slugs.Fabriquer(x.Category), V = Slugs.Fabriquer(x.Location) })
            .Where(g => g.Key.M != "" && g.Key.V != "")
            .Select(g => new
            {
                metier = g.Key.M,
                ville = g.Key.V,
                // Le libelle le plus frequent fait foi pour l'affichage :
                // c'est celui que les recruteurs ecrivent le plus souvent.
                libelleMetier = g.OrderByDescending(x => x.Nombre).First().Category,
                libelleVille = g.OrderByDescending(x => x.Nombre).First().Location,
                nombre = g.Sum(x => x.Nombre),
            })
            .Where(x => x.nombre >= OffresMinimum)
            .OrderByDescending(x => x.nombre)
            .Take(Math.Clamp(limite, 1, 5_000))
            .ToList();

        return Ok(groupees);
    }

    /// <summary>
    /// Une page d'atterrissage : le metier seul, ou le metier dans une
    /// ville.
    ///
    /// Rend 404 quand la combinaison n'atteint pas le seuil. C'est
    /// volontaire : mieux vaut une adresse qui n'existe pas qu'une page
    /// vide indexee.
    /// </summary>
    [HttpGet("{metier}")]
    [HttpGet("{metier}/{ville}")]
    [OutputCache(PolicyName = "catalogue")]
    public async Task<IActionResult> Page(string metier, string? ville = null)
    {
        var offres = await Visibles()
            .OrderByDescending(o => o.IsFeatured)
            .ThenByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id, o.Title, o.Company, o.Location, o.Category, o.ContractType,
                o.Salary, o.MinSalary, o.MaxSalary, o.IsRemote, o.IsFeatured,
                o.EasyApply, o.CreatedAt, o.Description, o.Benefits,
            })
            .Take(5_000)
            .ToListAsync();

        var retenues = offres
            .Where(o => Slugs.Fabriquer(o.Category) == metier)
            .Where(o => ville is null || Slugs.Fabriquer(o.Location) == ville)
            .ToList();

        if (retenues.Count < OffresMinimum) return NotFound();

        // Les villes voisines : c'est ce qui transforme une page
        // d'atterrissage en point d'entree plutot qu'en impasse.
        var autresVilles = offres
            .Where(o => Slugs.Fabriquer(o.Category) == metier)
            .GroupBy(o => Slugs.Fabriquer(o.Location))
            .Where(g => g.Key != "" && g.Key != ville && g.Count() >= OffresMinimum)
            .OrderByDescending(g => g.Count())
            .Take(12)
            .Select(g => new { ville = g.Key, libelle = g.First().Location, nombre = g.Count() })
            .ToList();

        var autresMetiers = ville is null
            ? new List<object>()
            : offres
                .Where(o => Slugs.Fabriquer(o.Location) == ville)
                .GroupBy(o => Slugs.Fabriquer(o.Category))
                .Where(g => g.Key != "" && g.Key != metier && g.Count() >= OffresMinimum)
                .OrderByDescending(g => g.Count())
                .Take(12)
                .Select(g => (object)new { metier = g.Key, libelle = g.First().Category, nombre = g.Count() })
                .ToList();

        var avecSalaire = retenues.Where(o => o.MinSalary is > 0).ToList();

        return Ok(new
        {
            metier,
            ville,
            libelleMetier = retenues[0].Category,
            libelleVille = ville is null ? null : retenues[0].Location,
            total = retenues.Count,
            // De quoi ecrire une phrase juste dans la description : un
            // chiffre invente serait pire que pas de chiffre du tout.
            salaireMedian = avecSalaire.Count >= 5
                ? avecSalaire.OrderBy(o => o.MinSalary).ElementAt(avecSalaire.Count / 2).MinSalary
                : null,
            partTeletravail = retenues.Count == 0 ? 0
                : (int)Math.Round(100.0 * retenues.Count(o => o.IsRemote) / retenues.Count),
            contrats = retenues
                .GroupBy(o => o.ContractType)
                .OrderByDescending(g => g.Count())
                .Select(g => new { contrat = g.Key, nombre = g.Count() })
                .ToList(),
            offres = retenues.Take(50),
            autresVilles,
            autresMetiers,
        });
    }
}
