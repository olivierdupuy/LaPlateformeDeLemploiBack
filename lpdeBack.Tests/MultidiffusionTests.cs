using Microsoft.Extensions.Configuration;
using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Tests;

/// <summary>
/// La multidiffusion, et surtout ses refus.
///
/// Le chemin qui aboutit demande un partenaire au bout du fil ; ce n'est
/// pas ce qui casse. Ce qui casse, ce sont les refus : un brouillon
/// poussé sans qu'on s'en aperçoive, une offre importée renvoyée à sa
/// propre source, un doublon créé par un second clic, un retrait qui
/// échoue et qu'on marque « retiré » quand même — celui-là étant le
/// plus grave, puisqu'il fait croire qu'une offre pourvue ne reçoit
/// plus de candidatures.
/// </summary>
public class MultidiffusionTests
{
    private static Multidiffusion Service(BaseEnMemoire b, bool partenaireConfigure = false)
    {
        var reglages = new Dictionary<string, string?>();
        if (partenaireConfigure)
        {
            reglages["Multidiffusion:PartenaireUrl"] = "https://partenaire.exemple/api";
            reglages["Multidiffusion:PartenaireJeton"] = "jeton-de-test";
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(reglages).Build();

        return new Multidiffusion(
            b.Contexte, config, new FabriqueInerte(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Multidiffusion>.Instance);
    }

    /// <summary>
    /// Une fabrique dont le client échoue à toute requête.
    ///
    /// Aucun test ci-dessous n'a besoin qu'un appel réussisse, et tous
    /// ont besoin qu'aucun ne parte pour de vrai. Une fabrique qui rend
    /// un vrai client ferait, un jour de distraction, une requête vers
    /// « partenaire.exemple » depuis l'intégration continue.
    /// </summary>
    private sealed class FabriqueInerte : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new GestionnaireInerte()) { BaseAddress = new Uri("https://partenaire.exemple") };

        private sealed class GestionnaireInerte : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("Aucun réseau depuis les tests.");
        }
    }

    // ══════════════════════════════════════════
    //  Ce qui est ouvert, et ce qui ne l'est pas
    // ══════════════════════════════════════════

    [Fact]
    public void Sans_identifiants_aucune_destination_n_est_ouverte()
    {
        using var b = new BaseEnMemoire();

        var diffusion = Service(b);

        Assert.False(diffusion.EstConfigure);
        Assert.All(diffusion.Destinations(), d => Assert.False(d.Configuree));
    }

    [Fact]
    public void Une_destination_fermee_dit_ce_qui_lui_manque()
    {
        using var b = new BaseEnMemoire();

        // Le parti du dépôt : inerte, et qui le dit. Un « non configuré »
        // sans le détail oblige à lire le code pour savoir quoi faire.
        foreach (var d in Service(b).Destinations())
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Manque));
        }
    }

    [Fact]
    public void Une_destination_configuree_n_a_plus_rien_a_reclamer()
    {
        using var b = new BaseEnMemoire();

        var partenaire = Service(b, partenaireConfigure: true)
            .Destinations().Single(d => d.Cle == "partenaire");

        Assert.True(partenaire.Configuree);
        Assert.Null(partenaire.Manque);
    }

    [Fact]
    public void L_etat_nomme_les_destinations_ouvertes()
    {
        using var b = new BaseEnMemoire();

        Assert.Contains("Aucune destination", Service(b).Etat);
        Assert.Contains("Partenaire", Service(b, partenaireConfigure: true).Etat);
    }

    // ══════════════════════════════════════════
    //  Les refus
    // ══════════════════════════════════════════

    [Fact]
    public async Task Une_destination_inconnue_est_une_erreur_d_appel()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = await Offre(b, "u1");

        await Assert.ThrowsAsync<ArgumentException>(
            () => Service(b).Diffuser(offre, "u1", "monster-en-2003"));
    }

    [Fact]
    public async Task Sans_configuration_la_diffusion_echoue_en_disant_pourquoi()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = await Offre(b, "u1");

        var suivi = await Service(b).Diffuser(offre, "u1", "partenaire");

        Assert.Equal("echec", suivi.Statut);
        Assert.Contains("Multidiffusion:PartenaireUrl", suivi.Motif);
        // Rien n'est parti : la tentative ne doit pas être décomptée.
        Assert.Equal(0, suivi.Tentatives);
    }

    [Fact]
    public async Task Un_brouillon_ne_se_diffuse_pas()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var brouillon = await Offre(b, "u1", brouillon: true);

        var suivi = await Service(b, partenaireConfigure: true)
            .Diffuser(brouillon, "u1", "partenaire");

        // Il n'a pas d'existence publique ici ; il n'en aura pas
        // davantage ailleurs.
        Assert.Equal("echec", suivi.Statut);
        Assert.Contains("brouillon", suivi.Motif, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Une_offre_importee_n_est_pas_renvoyee_a_sa_source()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var importee = await Offre(b, "u1", source: "France Travail");

        var suivi = await Service(b, partenaireConfigure: true)
            .Diffuser(importee, "u1", "partenaire");

        Assert.Equal("echec", suivi.Statut);
        Assert.Contains("France Travail", suivi.Motif);
        Assert.Contains("doublon", suivi.Motif);
    }

    [Fact]
    public async Task Une_offre_inexistante_echoue_proprement()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");

        var suivi = await Service(b, partenaireConfigure: true)
            .Diffuser(999_999, "u1", "partenaire");

        Assert.Equal("echec", suivi.Statut);
        Assert.Contains("introuvable", suivi.Motif);
    }

    [Fact]
    public async Task France_Travail_dit_qu_il_manque_l_habilitation()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = await Offre(b, "u1");

        var suivi = await Service(b).Diffuser(offre, "u1", "france-travail");

        Assert.Equal("echec", suivi.Statut);
        Assert.Contains("habilitation", suivi.Motif, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════
    //  Les tentatives
    // ══════════════════════════════════════════

    [Fact]
    public async Task Un_partenaire_injoignable_compte_une_tentative_et_rend_son_motif()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = await Offre(b, "u1");

        var suivi = await Service(b, partenaireConfigure: true)
            .Diffuser(offre, "u1", "partenaire");

        Assert.Equal("echec", suivi.Statut);
        Assert.Equal(1, suivi.Tentatives);
        Assert.Contains("Partenaire agregateur a refuse", suivi.Motif);
    }

    [Fact]
    public async Task On_n_insiste_pas_au_dela_du_plafond()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = await Offre(b, "u1");
        var diffusion = Service(b, partenaireConfigure: true);

        for (var i = 0; i < Multidiffusion.TentativesMax; i++)
            await diffusion.Diffuser(offre, "u1", "partenaire");

        var apres = await diffusion.Diffuser(offre, "u1", "partenaire");

        // Réessayer sans fin transforme une panne chez eux en charge
        // chez nous : le compteur ne bouge plus, et le motif le dit.
        Assert.Equal("echec", apres.Statut);
        Assert.Equal(Multidiffusion.TentativesMax, apres.Tentatives);
        Assert.Contains("Abandonne", apres.Motif);
    }

    [Fact]
    public async Task Une_seule_ligne_de_suivi_par_offre_et_par_destination()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = await Offre(b, "u1");
        var diffusion = Service(b, partenaireConfigure: true);

        await diffusion.Diffuser(offre, "u1", "partenaire");
        await diffusion.Diffuser(offre, "u1", "partenaire");

        // Un second clic ne crée pas une seconde diffusion : le
        // partenaire en ferait un doublon, ce que la déduplication du
        // catalogue passe son temps à nettoyer chez les autres.
        var suivi = await diffusion.Suivi(offre);
        Assert.Single(suivi);
    }

    // ══════════════════════════════════════════
    //  Le retrait
    // ══════════════════════════════════════════

    [Fact]
    public async Task Retirer_une_offre_jamais_diffusee_ne_rend_rien()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = await Offre(b, "u1");

        Assert.Null(await Service(b, partenaireConfigure: true).Retirer(offre, "partenaire"));
    }

    [Fact]
    public async Task Un_retrait_qui_echoue_ne_se_declare_pas_reussi()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = await Offre(b, "u1");

        // Une diffusion aboutie, posée à la main : c'est l'état d'où
        // part un retrait.
        b.Contexte.Diffusions.Add(new Diffusion
        {
            JobOfferId = offre,
            DemandeeParUserId = "u1",
            Destination = "partenaire",
            Statut = "diffusee",
            ReferenceExterne = "ext-42",
            DiffuseeLe = DateTime.UtcNow.AddDays(-1),
        });
        await b.Contexte.SaveChangesAsync();

        var suivi = await Service(b, partenaireConfigure: true).Retirer(offre, "partenaire");

        // C'est le point le plus important de tout ce fichier. Marquer
        // « retiree » un retrait qui a échoué ferait croire au recruteur
        // que son offre pourvue ne reçoit plus de candidatures, alors
        // qu'elle est toujours en ligne ailleurs.
        Assert.NotNull(suivi);
        Assert.Equal("diffusee", suivi!.Statut);
        Assert.Null(suivi.RetireeLe);
        Assert.Contains("toujours en ligne", suivi.Motif);
    }

    [Fact]
    public async Task Retirer_partout_ne_compte_que_ce_qui_est_reellement_retire()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = await Offre(b, "u1");

        b.Contexte.Diffusions.Add(new Diffusion
        {
            JobOfferId = offre, DemandeeParUserId = "u1", Destination = "partenaire",
            Statut = "diffusee", ReferenceExterne = "ext-42",
        });
        await b.Contexte.SaveChangesAsync();

        var retirees = await Service(b, partenaireConfigure: true).RetirerPartout(offre);

        Assert.Equal(0, retirees);
    }

    // ══════════════════════════════════════════

    private static async Task<int> Offre(BaseEnMemoire b, string auteur,
                                         bool brouillon = false, string? source = null)
    {
        var offre = new JobOffer
        {
            Title = "Developpeur",
            Company = "TechCorp",
            Location = "Marseille",
            Description = "Un poste.",
            ContractType = "CDI",
            CreatedByUserId = auteur,
            IsActive = true,
            IsDraft = brouillon,
            ExternalSource = source,
        };

        b.Contexte.JobOffers.Add(offre);
        await b.Contexte.SaveChangesAsync();
        return offre.Id;
    }
}
