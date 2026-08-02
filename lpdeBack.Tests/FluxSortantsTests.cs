using System.Net;

namespace lpdeBack.Tests;

/// <summary>
/// Les flux sortants.
///
/// Ils ne servent personne qui visite le site : ils servent les
/// agregateurs, qui les relisent sans que personne ne regarde. Un flux
/// casse ne se remarque donc pas — il se traduit, des semaines plus
/// tard, par une audience qui n'arrive plus, sans qu'aucun ecran ne
/// signale quoi que ce soit.
///
/// C'est exactement ce qui est arrive : « offres.xml » rendait 500 en
/// production, et rien ne le disait. Le flux JSON-LD, lui, repondait —
/// ce qui rendait la panne d'autant plus discrete.
/// </summary>
[Collection(CollectionApi.Nom)]
public class FluxSortantsTests
{
    private readonly ApiEnMemoire _api;

    public FluxSortantsTests(ApiEnMemoire api) => _api = api;

    [Theory]
    [InlineData("/api/flux/offres.xml", "xml")]
    [InlineData("/api/flux/offres.jsonld", "json")]
    public async Task Un_flux_repond_meme_sans_offre_a_diffuser(string chemin, string type)
    {
        // Le catalogue vide est le cas le plus simple, et il suffisait a
        // reveler la panne : elle etait dans l'ecriture du document,
        // pas dans les offres.
        var reponse = await _api.ClientAnonyme().GetAsync(chemin);

        Assert.True(reponse.IsSuccessStatusCode,
            $"{chemin} a repondu {(int)reponse.StatusCode}.");
        Assert.Contains(type, reponse.Content.Headers.ContentType?.MediaType ?? "");
    }

    /// <summary>
    /// Le flux est mis en cache dix minutes, et la politique varie sur
    /// tous les parametres de requete : un jeton unique suffit donc a
    /// obtenir un rendu frais. Sans lui, chaque test relirait la reponse
    /// que le precedent a laissee — et verifierait le cache plutot que
    /// le code.
    /// </summary>
    private static string Frais(string chemin, string jeton) => $"{chemin}?t={jeton}";

    [Fact]
    public async Task Le_flux_XML_est_un_document_bien_forme()
    {
        var recruteur = await _api.Compte("flux-recruteur", "Recruiter");
        await _api.Offre(recruteur.Id, titre: "Développeur Full Stack");

        var corps = await _api.ClientAnonyme()
            .GetStringAsync(Frais("/api/flux/offres.xml", "bien-forme"));

        // Se relire soi-meme : un agregateur qui rencontre un document
        // mal forme abandonne le flux entier, pas seulement l'offre
        // fautive.
        var document = System.Xml.Linq.XDocument.Parse(corps);

        Assert.Equal("source", document.Root!.Name.LocalName);
        Assert.Contains("Développeur Full Stack", corps);
    }

    [Fact]
    public async Task Le_prologue_annonce_l_encodage_reellement_servi()
    {
        var recruteur = await _api.Compte("flux-encodage", "Recruiter");
        await _api.Offre(recruteur.Id, titre: "Chargé de médiation");

        var reponse = await _api.ClientAnonyme()
            .GetAsync(Frais("/api/flux/offres.xml", "encodage"));
        var corps = await reponse.Content.ReadAsStringAsync();

        // Le prologue annoncait « utf-16 » — l'encodage d'une chaine
        // .NET — dans un document servi en UTF-8. Un agregateur qui
        // telecharge le fichier puis le traite sans son en-tete HTTP se
        // fie au prologue : il rejette le document, ou rend
        // « Chargé de médiation » pour tout le catalogue.
        Assert.Contains("utf-8", corps[..60], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("utf-16", corps[..60], StringComparison.OrdinalIgnoreCase);

        Assert.Equal("utf-8", reponse.Content.Headers.ContentType?.CharSet);
        Assert.Contains("Chargé de médiation", corps);
    }

    [Fact]
    public async Task Le_flux_XML_ne_rediffuse_pas_ce_qui_a_ete_importe()
    {
        var recruteur = await _api.Compte("flux-import", "Recruiter");
        var offre = await _api.Offre(recruteur.Id, titre: "Poste importé de chez eux");

        await _api.DansLaBase(async db =>
        {
            var o = await db.JobOffers.FindAsync(offre);
            o!.ExternalSource = "France Travail";
            return await db.SaveChangesAsync();
        });

        var corps = await _api.ClientAnonyme()
            .GetStringAsync(Frais("/api/flux/offres.xml", "import"));

        // Renvoyer aux agregateurs leurs propres annonces nous ferait
        // passer pour un revendeur de contenu, ce que les moteurs
        // sanctionnent.
        Assert.DoesNotContain("Poste importé de chez eux", corps);
    }

    [Fact]
    public async Task Le_plan_de_site_repond()
    {
        var reponse = await _api.ClientAnonyme().GetAsync("/api/seo/sitemap.xml");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }
}
