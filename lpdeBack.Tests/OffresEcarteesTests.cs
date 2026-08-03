using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Dire non, et que ce non tienne.
///
/// Le catalogue ramene cent vingt mille annonces et les memes reviennent
/// a chaque visite. Deux facons de s'en debarrasser : ecarter une offre
/// precise, ou ecarter une famille de metiers une fois pour toutes.
///
/// Le test qui compte est celui du filtrage effectif. Un bouton
/// « ne m'interesse pas » qui enregistre bien mais ne filtre pas est pire
/// que pas de bouton du tout : le candidat croit avoir agi, l'offre
/// revient, et il en conclut que le site ne l'ecoute pas.
/// </summary>
[Collection(CollectionApi.Nom)]
public class OffresEcarteesTests
{
    private readonly ApiEnMemoire _api;

    public OffresEcarteesTests(ApiEnMemoire api) => _api = api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<int[]> Catalogue(AppUser qui, string suite = "")
    {
        var r = await _api.ClientPour(qui).GetAsync($"/api/joboffers?pageSize=100{suite}");
        r.EnsureSuccessStatusCode();
        var liste = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);
        return liste.EnumerateArray().Select(o => o.GetProperty("id").GetInt32()).ToArray();
    }

    // ══════════════════════════════════════
    //  Ecarter une offre
    // ══════════════════════════════════════

    [Fact]
    public async Task Une_offre_ecartee_disparait_du_catalogue()
    {
        var rec = await _api.Compte("ec-rec", "Recruiter", "Maison A");
        var cand = await _api.Compte("ec-cand", "Candidate");
        var offre = await _api.Offre(rec.Id);

        Assert.Contains(offre, await Catalogue(cand));

        var r = await _api.ClientPour(cand).PostAsync($"/api/candidate/offres-ecartees/{offre}", null);
        r.EnsureSuccessStatusCode();

        Assert.DoesNotContain(offre, await Catalogue(cand));
    }

    [Fact]
    public async Task Le_geste_est_reversible()
    {
        var rec = await _api.Compte("ec-rev-rec", "Recruiter", "Maison A");
        var cand = await _api.Compte("ec-rev-cand", "Candidate");
        var offre = await _api.Offre(rec.Id);
        var client = _api.ClientPour(cand);

        await client.PostAsync($"/api/candidate/offres-ecartees/{offre}", null);
        await client.DeleteAsync($"/api/candidate/offres-ecartees/{offre}");

        Assert.Contains(offre, await Catalogue(cand));
    }

    [Fact]
    public async Task Ecarter_deux_fois_ne_leve_pas()
    {
        // Un double clic ne doit pas rendre une erreur : l'unicite est
        // posee en base, et le controleur doit la respecter sans y buter.
        var rec = await _api.Compte("ec-double-rec", "Recruiter", "Maison A");
        var cand = await _api.Compte("ec-double-cand", "Candidate");
        var offre = await _api.Offre(rec.Id);
        var client = _api.ClientPour(cand);

        await client.PostAsync($"/api/candidate/offres-ecartees/{offre}", null);
        var seconde = await client.PostAsync($"/api/candidate/offres-ecartees/{offre}", null);

        Assert.True(seconde.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Ce_qu_un_candidat_ecarte_ne_gene_personne_d_autre()
    {
        var rec = await _api.Compte("ec-autre-rec", "Recruiter", "Maison A");
        var un = await _api.Compte("ec-autre-1", "Candidate");
        var deux = await _api.Compte("ec-autre-2", "Candidate");
        var offre = await _api.Offre(rec.Id);

        await _api.ClientPour(un).PostAsync($"/api/candidate/offres-ecartees/{offre}", null);

        Assert.Contains(offre, await Catalogue(deux));
    }

    [Fact]
    public async Task Le_visiteur_anonyme_voit_tout()
    {
        // Le filtre ne s'applique qu'a un candidat identifie. Un anonyme
        // n'a rien declare, et amputer son catalogue serait absurde.
        var rec = await _api.Compte("ec-anon-rec", "Recruiter", "Maison A");
        var cand = await _api.Compte("ec-anon-cand", "Candidate");
        var offre = await _api.Offre(rec.Id);

        await _api.ClientPour(cand).PostAsync($"/api/candidate/offres-ecartees/{offre}", null);

        var r = await _api.ClientAnonyme().GetAsync("/api/joboffers?pageSize=100");
        var liste = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);
        var ids = liste.EnumerateArray().Select(o => o.GetProperty("id").GetInt32()).ToArray();

        Assert.Contains(offre, ids);
    }

    [Fact]
    public async Task Ecarter_une_offre_qui_n_existe_pas_rend_introuvable()
    {
        var cand = await _api.Compte("ec-fantome", "Candidate");

        var r = await _api.ClientPour(cand).PostAsync("/api/candidate/offres-ecartees/99999999", null);

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ══════════════════════════════════════
    //  Ecarter une famille de metiers
    // ══════════════════════════════════════

    [Fact]
    public async Task Une_famille_ecartee_ne_retient_que_les_familles_connues()
    {
        // Un mot libre ne filtrerait rien, et le candidat croirait avoir
        // ecarte quelque chose. Mieux vaut ne rien retenir que de retenir
        // ce qui ne sert a rien.
        var cand = await _api.Compte("ec-famille-inconnue", "Candidate");
        var client = _api.ClientPour(cand);

        await client.PutAsJsonAsync("/api/candidate/preferences",
            new { metiersExclus = new[] { "chasseur de dragons" } });

        var r = await client.GetAsync("/api/candidate/preferences");
        var corps = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);

        Assert.Empty(corps.GetProperty("metiersExclus").EnumerateArray());
    }

    [Fact]
    public async Task Une_famille_connue_est_retenue_et_rendue()
    {
        var cand = await _api.Compte("ec-famille-connue", "Candidate");
        var client = _api.ClientPour(cand);

        var famille = lpdeBack.Services.LexiqueMetiers.Familles.First();
        await client.PutAsJsonAsync("/api/candidate/preferences", new { metiersExclus = new[] { famille } });

        var r = await client.GetAsync("/api/candidate/preferences");
        var corps = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);
        var rendus = corps.GetProperty("metiersExclus").EnumerateArray().Select(x => x.GetString()).ToArray();

        Assert.Contains(famille, rendus);
    }

    [Fact]
    public async Task Des_metiers_exclus_suffisent_a_declarer_des_preferences()
    {
        // Sans cela, quelqu'un qui n'aurait fait qu'ecarter des familles
        // se verrait encore imposer les souhaits deduits de sa derniere
        // recherche : il aurait parle sans etre entendu.
        var cand = await _api.Compte("ec-famille-declaree", "Candidate");
        var client = _api.ClientPour(cand);

        await client.PutAsJsonAsync("/api/candidate/preferences",
            new { metiersExclus = new[] { lpdeBack.Services.LexiqueMetiers.Familles.First() } });

        var r = await client.GetAsync("/api/candidate/preferences");
        var corps = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);

        Assert.True(corps.GetProperty("declarees").GetBoolean());
    }
}
