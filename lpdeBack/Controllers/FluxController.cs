using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Le catalogue, sous une forme que les partenaires savent lire.
///
/// Un site d'emploi qui n'exporte rien n'existe que pour ceux qui
/// connaissent son adresse. Les agregateurs, eux, ne viennent pas
/// lire des pages : ils attendent un fichier, au format qu'ils
/// pratiquent depuis quinze ans.
///
/// Deux formats, parce qu'ils repondent a deux besoins :
///
///   Le flux « source » (XML plat, format historique d'Indeed, repris
///   par la plupart des agregateurs francais) sert a diffuser.
///
///   Le flux structure (JSON-LD, une entree « JobPosting » par offre)
///   sert a l'indexation par Google for Jobs, qui lit deja nos pages
///   mais gagne a recevoir la liste complete.
///
/// Les deux ne servent que les offres deposees chez nous. Rediffuser
/// ce qu'on a importe de France Travail ou d'Adzuna serait renvoyer
/// aux agregateurs leurs propres annonces, et nous ferait passer pour
/// un moulin a doublons.
/// </summary>
[ApiController]
[Route("api/flux")]
[AllowAnonymous]
public class FluxController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public FluxController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    private string Site => (_config["Seo:SiteUrl"] ?? "https://www.laplateformedelemploi.com").TrimEnd('/');

    /// <summary>
    /// Plafond par flux. Au-dela, les agregateurs decoupent eux-memes,
    /// et un fichier de cinquante mega-octets echoue plus souvent qu'il
    /// n'aboutit.
    /// </summary>
    private const int Plafond = 5_000;

    /// <summary>
    /// Flux XML des offres deposees sur la plateforme.
    ///
    /// Le format impose des noms d'elements en anglais et des sections
    /// litterales : c'est celui que les agregateurs lisent, on ne le
    /// francise pas.
    /// </summary>
    [HttpGet("offres.xml")]
    [OutputCache(PolicyName = "reference")]
    public async Task<IActionResult> Xml()
    {
        var offres = await _context.JobOffers
            .AsNoTracking()
            .Where(o => o.IsActive && !o.IsDraft
                        && o.ModerationStatus == "Approved"
                        && o.ExternalSource == null)
            .OrderByDescending(o => o.CreatedAt)
            .Take(Plafond)
            .ToListAsync();

        var sortie = new EcrivainUtf8();
        var reglages = new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
            // Une description d'offre peut contenir des caracteres de
            // controle venus d'un copier-coller Word : ils rendent le
            // fichier illisible pour l'agregateur, qui abandonne tout le
            // flux plutot que l'offre fautive.
            CheckCharacters = false,
        };

        // « using » et non « await using ».
        //
        // Le flux rendait 500 en production, et seulement la : la
        // liberation asynchrone d'un XmlWriter exige
        // « XmlWriterSettings.Async = true », faute de quoi elle leve a
        // la fermeture — apres que tout le document a ete ecrit sans
        // erreur. Toutes les ecritures ci-dessous sont synchrones ;
        // c'est donc la fermeture qui doit l'etre, pas les reglages qui
        // doivent devenir asynchrones.
        //
        // Personne ne l'avait vu parce que personne ne lit ce flux :
        // il sert les agregateurs, qui le relisent sans que cela se
        // remarque. Une panne s'y traduit, des semaines plus tard, par
        // une audience qui n'arrive plus.
        using (var ecrivain = XmlWriter.Create(sortie, reglages))
        {
            ecrivain.WriteStartDocument();
            ecrivain.WriteStartElement("source");

            ecrivain.WriteElementString("publisher", "La plateforme de l'emploi");
            ecrivain.WriteElementString("publisherurl", Site);
            ecrivain.WriteElementString("lastBuildDate", DateTime.UtcNow.ToString("r"));

            foreach (var o in offres)
            {
                ecrivain.WriteStartElement("job");

                Cdata(ecrivain, "title", o.Title);
                Cdata(ecrivain, "date", o.CreatedAt.ToString("r"));
                Cdata(ecrivain, "referencenumber", o.Id.ToString());
                Cdata(ecrivain, "url", $"{Site}/offres/{o.Id}");
                Cdata(ecrivain, "company", o.Company);
                Cdata(ecrivain, "city", o.Location);
                Cdata(ecrivain, "country", "FR");
                Cdata(ecrivain, "description", Nettoyer(o.Description));
                Cdata(ecrivain, "jobtype", o.ContractType);
                Cdata(ecrivain, "category", o.Category);

                if (o.MinSalary is > 0 || o.MaxSalary is > 0)
                    Cdata(ecrivain, "salary", Salaire(o.MinSalary, o.MaxSalary, o.SalaryPeriod));

                if (o.IsRemote) Cdata(ecrivain, "remotetype", "Fully remote");
                if (o.ExpiresAt is not null) Cdata(ecrivain, "expirationdate", o.ExpiresAt.Value.ToString("r"));

                ecrivain.WriteEndElement();
            }

            ecrivain.WriteEndElement();
            ecrivain.WriteEndDocument();
        }

        // Le flux fait plusieurs mega-octets et les agregateurs le
        // relisent plusieurs fois par jour. L'etiquette leur permet de
        // repartir sans corps quand rien n'a bouge.
        return this.AvecEtiquette(sortie.ToString(), "application/xml; charset=utf-8");
    }

    /// <summary>
    /// Le meme catalogue en JSON-LD, une entree « JobPosting » par
    /// offre. Google for Jobs lit deja nos pages ; ce flux lui donne la
    /// liste complete d'un coup, ce qui compte quand elle change tous
    /// les jours.
    /// </summary>
    [HttpGet("offres.jsonld")]
    [OutputCache(PolicyName = "reference")]
    public async Task<IActionResult> JsonLd()
    {
        var offres = await _context.JobOffers
            .AsNoTracking()
            .Where(o => o.IsActive && !o.IsDraft
                        && o.ModerationStatus == "Approved"
                        && o.ExternalSource == null)
            .OrderByDescending(o => o.CreatedAt)
            .Take(Plafond)
            .ToListAsync();

        var entrees = offres.Select(o => new Dictionary<string, object?>
        {
            ["@type"] = "JobPosting",
            ["title"] = o.Title,
            ["description"] = Nettoyer(o.Description),
            ["datePosted"] = o.CreatedAt.ToString("yyyy-MM-dd"),
            ["validThrough"] = o.ExpiresAt?.ToString("yyyy-MM-dd"),
            ["employmentType"] = TypeEmploi(o.ContractType),
            ["url"] = $"{Site}/offres/{o.Id}",
            ["identifier"] = new { @type = "PropertyValue", name = "La plateforme de l'emploi", value = o.Id.ToString() },
            ["hiringOrganization"] = new { @type = "Organization", name = o.Company },
            ["jobLocation"] = new
            {
                @type = "Place",
                address = new { @type = "PostalAddress", addressLocality = o.Location, addressCountry = "FR" },
            },
            ["jobLocationType"] = o.IsRemote ? "TELECOMMUTE" : null,
            ["baseSalary"] = o.MinSalary is > 0 ? new
            {
                @type = "MonetaryAmount",
                currency = "EUR",
                value = new
                {
                    @type = "QuantitativeValue",
                    minValue = o.MinSalary,
                    maxValue = o.MaxSalary ?? o.MinSalary,
                    unitText = (o.SalaryPeriod ?? "year").ToUpperInvariant() == "MONTH" ? "MONTH" : "YEAR",
                },
            } : null,
        });

        return Ok(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "ItemList",
            ["numberOfItems"] = offres.Count,
            ["itemListElement"] = entrees,
        });
    }

    /// <summary>
    /// Un ecrivain de chaine qui se declare en UTF-8.
    ///
    /// <see cref="XmlWriter"/> prend l'encodage du prologue chez
    /// l'ecrivain, et non dans ses reglages, quand il ecrit vers une
    /// chaine — une chaine .NET etant en UTF-16, il annoncait
    /// « encoding="utf-16" » dans un document servi en UTF-8, et le
    /// <c>Encoding = Encoding.UTF8</c> des reglages n'y changeait rien.
    ///
    /// Un analyseur qui lit le fichier sans son en-tete HTTP — c'est le
    /// cas des agregateurs, qui le telechargent puis le traitent — se
    /// fie au prologue. Il rejette le document, ou decode les accents
    /// de travers : « Développeur » pour tout le catalogue.
    /// </summary>
    private sealed class EcrivainUtf8 : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    private static void Cdata(XmlWriter ecrivain, string nom, string? valeur)
    {
        ecrivain.WriteStartElement(nom);
        ecrivain.WriteCData(valeur?.Replace("]]>", "]] >") ?? string.Empty);
        ecrivain.WriteEndElement();
    }

    /// <summary>
    /// Les caracteres de controle viennent des copier-coller depuis un
    /// traitement de texte. Un agregateur qui en rencontre un abandonne
    /// le flux entier, pas seulement l'offre.
    /// </summary>
    private static string Nettoyer(string? texte)
    {
        if (string.IsNullOrEmpty(texte)) return string.Empty;
        var sortie = new StringBuilder(texte.Length);
        foreach (var c in texte)
            if (c is '\n' or '\r' or '\t' || !char.IsControl(c)) sortie.Append(c);
        return sortie.ToString();
    }

    private static string Salaire(int? min, int? max, string? periode)
    {
        var unite = (periode ?? "year").ToLowerInvariant() == "month" ? "par mois" : "par an";
        if (min is > 0 && max is > 0 && max != min) return $"{min} - {max} EUR {unite}";
        return $"{min ?? max} EUR {unite}";
    }

    /// <summary>Le vocabulaire de schema.org, qui ne connait pas le CDD.</summary>
    private static string TypeEmploi(string? contrat) => (contrat ?? "").ToLowerInvariant() switch
    {
        "cdi" => "FULL_TIME",
        "cdd" => "TEMPORARY",
        "stage" => "INTERN",
        "alternance" => "OTHER",
        "freelance" => "CONTRACTOR",
        "interim" => "TEMPORARY",
        _ => "FULL_TIME",
    };
}
