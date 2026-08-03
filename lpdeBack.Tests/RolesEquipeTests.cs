using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Qui peut toucher au travail de qui.
///
/// Le partage etait binaire : declarer la meme entreprise suffisait a
/// pouvoir modifier, suspendre et supprimer les offres de tout le monde.
/// Cela convient a deux associes, pas a une equipe de dix — un nouvel
/// arrivant avait le catalogue entier a sa main des sa premiere
/// connexion.
///
/// La distinction porte sur l'ECRITURE seule. La lecture reste partagee :
/// c'est elle qui fait l'interet du travail a plusieurs, et la restreindre
/// rendrait l'equipe inutile. Ces tests tiennent les deux moities de cette
/// phrase, parce qu'il serait facile de casser la seconde en durcissant
/// la premiere.
/// </summary>
[Collection(CollectionApi.Nom)]
public class RolesEquipeTests
{
    private readonly ApiEnMemoire _api;

    public RolesEquipeTests(ApiEnMemoire api) => _api = api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<AppUser> Recruteur(string graine, string maison, string role)
    {
        var u = await _api.Compte(graine, "Recruiter", maison);
        await _api.DansLaBase(async db =>
        {
            var c = await db.Users.FindAsync(u.Id);
            c!.RoleEquipe = role;
            return await db.SaveChangesAsync();
        });
        return u;
    }

    private async Task<HttpResponseMessage> Suspendre(AppUser qui, int offre) =>
        await _api.ClientPour(qui).PatchAsJsonAsync($"/api/joboffers/{offre}/etat",
            new { etat = EtatOffre.Suspendue });

    // ══════════════════════════════════════
    //  L'ecriture se restreint
    // ══════════════════════════════════════

    [Fact]
    public async Task Un_membre_ne_touche_pas_a_l_offre_d_un_collegue()
    {
        var membre = await Recruteur("re-membre", "Maison Roles", RolesEquipe.Membre);
        var autre = await Recruteur("re-collegue", "Maison Roles", RolesEquipe.Membre);
        var offre = await _api.Offre(autre.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await Suspendre(membre, offre)).StatusCode);
    }

    [Fact]
    public async Task Un_membre_gere_ses_propres_offres()
    {
        // Ce qui est a moi reste a moi, quel que soit mon role : sans cela
        // la restriction empecherait de travailler.
        var membre = await Recruteur("re-sienne", "Maison Roles", RolesEquipe.Membre);
        var offre = await _api.Offre(membre.Id);

        Assert.True((await Suspendre(membre, offre)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Un_proprietaire_gere_les_offres_de_l_equipe()
    {
        var patron = await Recruteur("re-patron", "Maison Patron", RolesEquipe.Proprietaire);
        var membre = await Recruteur("re-son-membre", "Maison Patron", RolesEquipe.Membre);
        var offre = await _api.Offre(membre.Id);

        Assert.True((await Suspendre(patron, offre)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Un_proprietaire_ne_deborde_pas_sur_une_autre_maison()
    {
        var patron = await Recruteur("re-patron2", "Maison X", RolesEquipe.Proprietaire);
        var etranger = await Recruteur("re-etranger", "Maison Y", RolesEquipe.Membre);
        var offre = await _api.Offre(etranger.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await Suspendre(patron, offre)).StatusCode);
    }

    // ══════════════════════════════════════
    //  La lecture reste partagee
    // ══════════════════════════════════════

    [Fact]
    public async Task Un_membre_voit_toujours_les_offres_de_l_equipe()
    {
        // La moitie qu'il serait facile de casser en durcissant l'autre.
        var membre = await Recruteur("re-lecture-1", "Maison Lecture", RolesEquipe.Membre);
        var autre = await Recruteur("re-lecture-2", "Maison Lecture", RolesEquipe.Membre);
        var offre = await _api.Offre(autre.Id);

        var r = await _api.ClientPour(membre).GetAsync("/api/joboffers/mine?scope=team");
        r.EnsureSuccessStatusCode();
        var liste = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);

        Assert.Contains(liste.EnumerateArray(), o => o.GetProperty("id").GetInt32() == offre);
    }

    // ══════════════════════════════════════
    //  La distribution des roles
    // ══════════════════════════════════════

    [Fact]
    public async Task Seul_un_proprietaire_distribue_les_roles()
    {
        var membre = await Recruteur("re-distrib-m", "Maison Distrib", RolesEquipe.Membre);
        var autre = await Recruteur("re-distrib-a", "Maison Distrib", RolesEquipe.Membre);

        var r = await _api.ClientPour(membre).PatchAsJsonAsync(
            $"/api/recruiter/equipe/{autre.Id}/role", new { role = RolesEquipe.Proprietaire });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Un_proprietaire_promeut_un_membre()
    {
        var patron = await Recruteur("re-prom-p", "Maison Prom", RolesEquipe.Proprietaire);
        var membre = await Recruteur("re-prom-m", "Maison Prom", RolesEquipe.Membre);

        var r = await _api.ClientPour(patron).PatchAsJsonAsync(
            $"/api/recruiter/equipe/{membre.Id}/role", new { role = RolesEquipe.Proprietaire });
        Assert.True(r.IsSuccessStatusCode);

        var role = await _api.DansLaBase(async db =>
            (await db.Users.FindAsync(membre.Id))!.RoleEquipe);
        Assert.Equal(RolesEquipe.Proprietaire, role);
    }

    [Fact]
    public async Task On_ne_se_retire_pas_soi_meme_la_propriete()
    {
        // Ce serait le seul geste irreversible de l'ecran : plus personne
        // ne pourrait la redonner.
        var patron = await Recruteur("re-suicide", "Maison Suicide", RolesEquipe.Proprietaire);

        var r = await _api.ClientPour(patron).PatchAsJsonAsync(
            $"/api/recruiter/equipe/{patron.Id}/role", new { role = RolesEquipe.Membre });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Une_equipe_garde_toujours_un_proprietaire()
    {
        // Sans cela, ses offres deviennent ingerables sauf par
        // l'administration du site.
        var patron = await Recruteur("re-dernier-p", "Maison Dernier", RolesEquipe.Proprietaire);
        var second = await Recruteur("re-dernier-2", "Maison Dernier", RolesEquipe.Proprietaire);

        // Retirer le second passe : il en reste un.
        var premier = await _api.ClientPour(patron).PatchAsJsonAsync(
            $"/api/recruiter/equipe/{second.Id}/role", new { role = RolesEquipe.Membre });
        Assert.True(premier.IsSuccessStatusCode);

        // Se retirer soi-meme ensuite est refuse par la regle precedente,
        // et retirer le dernier par celle-ci.
        await _api.DansLaBase(async db =>
        {
            var u = await db.Users.FindAsync(second.Id);
            u!.RoleEquipe = RolesEquipe.Proprietaire;
            var p = await db.Users.FindAsync(patron.Id);
            p!.RoleEquipe = RolesEquipe.Membre;
            return await db.SaveChangesAsync();
        });

        var dernier = await _api.ClientPour(second).PatchAsJsonAsync(
            $"/api/recruiter/equipe/{second.Id}/role", new { role = RolesEquipe.Membre });
        Assert.Equal(HttpStatusCode.BadRequest, dernier.StatusCode);
    }

    [Fact]
    public async Task L_equipe_est_visible_de_tous_ses_membres()
    {
        // Savoir a qui s'adresser pour faire modifier une offre qu'on ne
        // peut pas toucher fait partie du reglage : le cacher
        // transformerait une restriction comprehensible en mur.
        var patron = await Recruteur("re-vue-p", "Maison Vue", RolesEquipe.Proprietaire);
        var membre = await Recruteur("re-vue-m", "Maison Vue", RolesEquipe.Membre);

        var r = await _api.ClientPour(membre).GetAsync("/api/recruiter/equipe");
        r.EnsureSuccessStatusCode();
        var corps = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);

        Assert.False(corps.GetProperty("jeSuisProprietaire").GetBoolean());
        Assert.Contains(corps.GetProperty("membres").EnumerateArray(),
            m => m.GetProperty("id").GetString() == patron.Id
                 && m.GetProperty("role").GetString() == RolesEquipe.Proprietaire);
    }
}
