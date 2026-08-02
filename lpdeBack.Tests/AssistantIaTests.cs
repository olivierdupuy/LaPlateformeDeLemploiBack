using lpdeBack.Models;
using lpdeBack.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lpdeBack.Tests;

/// <summary>
/// La degradation, qui est la seule chose vraiment importante ici.
///
/// Tout ce que le site fait d'intelligent est calcule par des regles.
/// L'assistance ajoute de la nuance a un resultat qui existe deja, et
/// c'est ce contrat qu'il faut tenir : cle absente, API tombee, quota
/// epuise — dans les trois cas le site continue, simplement moins bavard.
///
/// Aucun de ces tests n'atteint le reseau. Le client est construit sans
/// fabrique de clients HTTP : tout appel qui parviendrait a sortir
/// leverait immediatement, ce qui est precisement ce qu'on veut verifier
/// — l'exception ne doit jamais remonter jusqu'a l'appelant.
/// </summary>
public class AssistantIaTests
{
    private const string RequeteDure =
        "je voudrais accompagner des personnes agees pas trop loin de chez moi";

    private static AssistantIa Assistant(
        string baseUrl = "", string modele = "", int plafond = AssistantIa.PlafondParDefaut,
        string cle = "sk-ant-factice", string dialecte = "anthropic")
    {
        var client = new AiClient(
            null!,
            Options.Create(new AiOptions
            {
                BaseUrl = baseUrl, Model = modele, Api = dialecte, ApiKey = cle,
            }),
            NullLogger<AiClient>.Instance);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:AppelsAssistesParJour"] = plafond.ToString(),
            })
            .Build();

        return new AssistantIa(
            client, new MemoryCache(new MemoryCacheOptions()), config,
            NullLogger<AssistantIa>.Instance);
    }

    /// <summary>Configure, mais injoignable : la fabrique HTTP est nulle.</summary>
    private static AssistantIa Injoignable(int plafond = AssistantIa.PlafondParDefaut) =>
        Assistant("https://exemple.invalide/v1", "claude-haiku-4-5", plafond);

    // ══════════════════════════════════════
    //  Sans cle
    // ══════════════════════════════════════

    [Fact]
    public void Sans_modele_configure_l_assistance_se_declare_indisponible()
    {
        Assert.False(Assistant().Disponible);
    }

    [Fact]
    public void Une_adresse_sans_cle_ne_vaut_pas_une_configuration()
    {
        // « appsettings.json » livre une adresse et un nom de modele
        // remplis, et une cle vide. Le site se croyait donc equipe :
        // il sortait vers api.anthropic.com, recevait un 401, et avait
        // deja decompte un appel du quota du jour.
        Assert.False(
            Assistant("https://api.anthropic.com/v1", "claude-haiku-4-5", cle: "").Disponible);
    }

    [Fact]
    public void Un_modele_du_reseau_local_n_a_pas_besoin_de_cle()
    {
        // Ollama et llama.cpp servent sans authentification. Exiger une
        // cle interdirait la seule configuration qui ne coute rien.
        Assert.True(
            Assistant("http://localhost:11434", "mistral", cle: "", dialecte: "ollama").Disponible);
    }

    [Fact]
    public async Task Sans_modele_la_relecture_rend_la_requete_intacte()
    {
        var regles = RequeteLibre.Analyser(RequeteDure);
        var apres = await Assistant().Relire(RequeteDure, regles);

        Assert.Equal(regles.Metier, apres.Metier);
        Assert.Equal(regles.Lieu, apres.Lieu);
        Assert.Equal(regles.Contrat, apres.Contrat);
        Assert.Equal(regles.Compris.Count, apres.Compris.Count);
    }

    [Fact]
    public async Task Sans_modele_le_resume_est_absent_et_non_une_erreur()
    {
        var note = new Rapprochement(72, 80, new[] { "4 competences en commun" }, new[] { "a 80 km" });
        Assert.Null(await Assistant().Resumer(note, "Developpeur front-end"));
    }

    [Fact]
    public async Task Sans_modele_l_avis_de_moderation_est_absent()
    {
        var offre = new JobOffer { Id = 1, Title = "Offre", Description = "Texte." };
        Assert.Null(await Assistant().Moderer(offre, 40, "description tres courte"));
    }

    // ══════════════════════════════════════
    //  Modele configure mais injoignable
    // ══════════════════════════════════════

    [Fact]
    public async Task Une_panne_du_modele_ne_remonte_jamais_a_l_appelant()
    {
        // Le service parle a une adresse qui n'existe pas, avec une
        // fabrique de clients HTTP nulle : l'appel leve. Rien ne doit
        // sortir d'ici — une exception transformerait un confort en panne
        // de page de resultats.
        var assistant = Injoignable();
        var regles = RequeteLibre.Analyser(RequeteDure);

        var requete = await assistant.Relire(RequeteDure, regles);
        var resume = await assistant.Resumer(
            new Rapprochement(72, 80, new[] { "raison" }, Array.Empty<string>()), "Poste");
        var avis = await assistant.Moderer(
            new JobOffer { Id = 2, Title = "Offre", Description = "Texte." }, 40, null);

        Assert.Equal(regles.Metier, requete.Metier);
        Assert.Null(resume);
        Assert.Null(avis);
    }

    // ══════════════════════════════════════
    //  Le plafond de depense
    // ══════════════════════════════════════

    [Fact]
    public void Un_plafond_epuise_suffit_a_couper_l_assistance()
    {
        // Meme cle valide, meme API debout : au-dela du quota du jour, le
        // site retombe sur ses regles. C'est le garde-fou contre la boucle
        // mal ecrite et le robot d'indexation.
        Assert.False(Injoignable(plafond: 0).Disponible);
    }

    [Fact]
    public async Task Une_requete_que_les_regles_tiennent_ne_consomme_rien()
    {
        // « developpeur react perpignan » est entierement comprise sans
        // modele. L'appeler quand meme serait de l'argent jete a chaque
        // frappe de clavier.
        var assistant = Injoignable(plafond: 5);
        const string facile = "developpeur react perpignan";
        var regles = RequeteLibre.Analyser(facile);

        var avant = assistant.Restant;
        var apres = await assistant.Relire(facile, regles);

        Assert.Equal(avant, assistant.Restant);
        Assert.Same(regles, apres);
    }

    [Fact]
    public async Task Une_requete_difficile_consomme_un_appel_et_un_seul()
    {
        var assistant = Injoignable(plafond: 5);
        var regles = RequeteLibre.Analyser(RequeteDure);

        var avant = assistant.Restant;
        await assistant.Relire(RequeteDure, regles);

        Assert.Equal(avant - 1, assistant.Restant);
    }

    [Fact]
    public async Task Le_plafond_atteint_les_appels_suivants_ne_partent_plus()
    {
        var assistant = Injoignable(plafond: 1);

        await assistant.Relire(RequeteDure, RequeteLibre.Analyser(RequeteDure));
        Assert.Equal(0, assistant.Restant);
        Assert.False(assistant.Disponible);

        // Une autre phrase, donc une autre cle de cache : sans plafond,
        // celle-ci partirait aussi.
        const string autre = "je cherche quelque chose dans la restauration pas trop loin";
        await assistant.Relire(autre, RequeteLibre.Analyser(autre));

        Assert.Equal(0, assistant.Restant);
    }
}
