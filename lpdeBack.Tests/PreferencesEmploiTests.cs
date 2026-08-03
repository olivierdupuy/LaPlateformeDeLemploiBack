using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Ce que le candidat cherche, declare plutot que devine.
///
/// La correspondance offre / candidat lisait la derniere recherche
/// enregistree pour en tirer un contrat vise et une envie de teletravail.
/// C'etait la meilleure source disponible, et elle reste le repli — mais
/// elle est muette pour qui n'a jamais enregistre de recherche, et
/// bavarde a contretemps pour qui en a enregistre une par curiosite.
///
/// Ce qui se teste ici n'est pas le formulaire : c'est la regle de
/// preseance entre le declare et le deduit, et les deux facons dont elle
/// peut se tromper — faire taire un repli encore utile, ou l'imposer a
/// quelqu'un qui a deja parle. Une regression sur ce point ne casse rien :
/// elle change silencieusement les scores affiches a tout le monde.
/// </summary>
[Collection(CollectionApi.Nom)]
public class PreferencesEmploiTests
{
    private readonly ApiEnMemoire _api;

    public PreferencesEmploiTests(ApiEnMemoire api) => _api = api;

    private const string Chemin = "/api/candidate/preferences";

    private static async Task<JsonElement> Lire(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(
            await r.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    /// <summary>Les souhaits reellement passes au moteur, declares ou deduits.</summary>
    private static JsonElement Effectifs(JsonElement corps) => corps.GetProperty("effectifs");

    private static string? Texte(JsonElement e, string nom) =>
        e.GetProperty(nom).ValueKind == JsonValueKind.Null ? null : e.GetProperty(nom).GetString();

    private static int? Nombre(JsonElement e, string nom) =>
        e.GetProperty(nom).ValueKind == JsonValueKind.Null ? null : e.GetProperty(nom).GetInt32();

    private async Task Recherche(AppUser compte, string? contrat, bool? distanciel)
    {
        await _api.DansLaBase(async db =>
        {
            db.SavedSearches.Add(new SavedSearch
            {
                UserId = compte.Id, Label = "essai",
                ContractType = contrat, IsRemote = distanciel,
            });
            return await db.SaveChangesAsync();
        });
    }

    // ══════════════════════════════════════
    //  Le repli sur la recherche enregistree
    // ══════════════════════════════════════

    [Fact]
    public async Task Sans_rien_de_declare_la_derniere_recherche_tient_lieu_de_souhaits()
    {
        // Le comportement d'avant, qui doit survivre : la plupart des
        // comptes n'ont pas de preferences et ne doivent rien perdre.
        var candidat = await _api.Compte("pref-repli", "Candidate");
        await Recherche(candidat, "CDI", true);

        var corps = await Lire(await _api.ClientPour(candidat).GetAsync(Chemin));

        Assert.False(corps.GetProperty("declarees").GetBoolean());
        Assert.Equal("deduites", corps.GetProperty("origine").GetString());
        Assert.Equal("CDI", Texte(Effectifs(corps), "contrat"));
    }

    [Fact]
    public async Task Sans_recherche_ni_preference_aucun_souhait_n_est_invente()
    {
        var candidat = await _api.Compte("pref-vierge", "Candidate");

        var effectifs = Effectifs(await Lire(await _api.ClientPour(candidat).GetAsync(Chemin)));

        Assert.Null(Texte(effectifs, "contrat"));
        Assert.Null(Nombre(effectifs, "salaireAnnuelMinimum"));
    }

    // ══════════════════════════════════════
    //  La preseance du declare
    // ══════════════════════════════════════

    [Fact]
    public async Task Ce_qui_est_declare_l_emporte_sur_ce_qui_etait_deduit()
    {
        // Une recherche faite un soir pour un ami ne doit pas survivre a
        // un choix explicite.
        var candidat = await _api.Compte("pref-preseance", "Candidate");
        await Recherche(candidat, "Stage", true);

        var client = _api.ClientPour(candidat);
        await client.PutAsJsonAsync(Chemin, new { contrat = "CDI", distanciel = false, salaireAnnuelMinimum = 38000 });

        var corps = await Lire(await client.GetAsync(Chemin));

        Assert.True(corps.GetProperty("declarees").GetBoolean());
        Assert.Equal("declarees", corps.GetProperty("origine").GetString());
        Assert.Equal("CDI", Texte(Effectifs(corps), "contrat"));
        Assert.Equal(38000, Nombre(Effectifs(corps), "salaireAnnuelMinimum"));
    }

    [Fact]
    public async Task Un_seul_champ_renseigne_suffit_a_couper_la_deduction()
    {
        // Un candidat qui n'a dit qu'un plancher de salaire a dit quelque
        // chose. Lui ajouter par deduction un contrat qu'il n'a pas choisi
        // reviendrait a inventer — et il chercherait longtemps d'ou sort
        // ce critere dans son score.
        var candidat = await _api.Compte("pref-partiel", "Candidate");
        await Recherche(candidat, "Alternance", true);

        var client = _api.ClientPour(candidat);
        await client.PutAsJsonAsync(Chemin, new { salaireAnnuelMinimum = 30000 });

        var effectifs = Effectifs(await Lire(await client.GetAsync(Chemin)));

        Assert.Equal(30000, Nombre(effectifs, "salaireAnnuelMinimum"));
        Assert.Null(Texte(effectifs, "contrat"));
    }

    [Fact]
    public async Task Des_preferences_entierement_vides_ne_font_pas_taire_le_repli()
    {
        // Ouvrir le formulaire et le refermer cree une ligne en base. Elle
        // ne dit rien, et ne doit donc rien empecher.
        var candidat = await _api.Compte("pref-vide", "Candidate");
        await Recherche(candidat, "CDD", null);

        var client = _api.ClientPour(candidat);
        await client.PutAsJsonAsync(Chemin, new { });

        var corps = await Lire(await client.GetAsync(Chemin));

        Assert.False(corps.GetProperty("declarees").GetBoolean());
        Assert.Equal("CDD", Texte(Effectifs(corps), "contrat"));
    }

    // ══════════════════════════════════════
    //  Les pieges de saisie
    // ══════════════════════════════════════

    [Fact]
    public async Task Un_contrat_vide_vaut_indifferent_et_non_un_contrat_sans_nom()
    {
        // Un « select » remis sur sa premiere ligne envoie une chaine
        // vide. Stockee telle quelle, le moteur chercherait des offres dont
        // le type de contrat est « » : il n'en existe aucune, et tous les
        // scores du candidat s'effondreraient sans explication.
        var candidat = await _api.Compte("pref-vide-contrat", "Candidate");
        var client = _api.ClientPour(candidat);

        await client.PutAsJsonAsync(Chemin, new { contrat = "   ", salaireAnnuelMinimum = 25000 });

        var corps = await Lire(await client.GetAsync(Chemin));

        Assert.Null(Texte(corps, "contrat"));
        Assert.Null(Texte(Effectifs(corps), "contrat"));
    }

    [Fact]
    public async Task Un_rayon_absurde_est_refuse()
    {
        var candidat = await _api.Compte("pref-rayon", "Candidate");

        var r = await _api.ClientPour(candidat).PutAsJsonAsync(Chemin, new { rayonKm = 5000 });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Enregistrer_deux_fois_ne_cree_pas_deux_jeux_de_preferences()
    {
        // L'unicite est posee en base, mais c'est le controleur qui la
        // respecte : sans la relecture avant ecriture, la seconde
        // sauvegarde leverait au lieu de mettre a jour.
        var candidat = await _api.Compte("pref-deux-fois", "Candidate");
        var client = _api.ClientPour(candidat);

        await client.PutAsJsonAsync(Chemin, new { contrat = "CDI" });
        await client.PutAsJsonAsync(Chemin, new { contrat = "CDD" });

        var corps = await Lire(await client.GetAsync(Chemin));
        Assert.Equal("CDD", Texte(corps, "contrat"));

        var combien = await _api.DansLaBase(async db =>
            await Task.FromResult(db.PreferencesEmploi.Count(p => p.UserId == candidat.Id)));
        Assert.Equal(1, combien);
    }

    // ══════════════════════════════════════
    //  Cloisonnement
    // ══════════════════════════════════════

    [Fact]
    public async Task Les_preferences_d_un_candidat_ne_debordent_pas_sur_un_autre()
    {
        var une = await _api.Compte("pref-une", "Candidate");
        var autre = await _api.Compte("pref-autre", "Candidate");

        await _api.ClientPour(une).PutAsJsonAsync(Chemin, new { contrat = "Freelance", rayonKm = 10 });

        var corps = await Lire(await _api.ClientPour(autre).GetAsync(Chemin));

        Assert.Null(Texte(corps, "contrat"));
        Assert.Null(Nombre(corps, "rayonKm"));
    }

    [Fact]
    public async Task Un_visiteur_anonyme_n_a_pas_de_preferences()
    {
        var r = await _api.ClientAnonyme().GetAsync(Chemin);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
