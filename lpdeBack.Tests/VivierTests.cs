using System.Text.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Le vivier de candidats, et une promesse qu'il ne tenait pas.
///
/// Le profil affiche un interrupteur « Visibilite aupres des
/// recruteurs », dont le libelle dit : « Vous n'apparaissez dans aucune
/// recherche de candidats ». Deux points d'entree servent pourtant ce
/// vivier, et un seul respectait le reglage — l'autre rendait le profil
/// masque comme les autres, sans erreur ni signal. Un candidat qui
/// s'etait retire restait donc visible d'un ecran sur deux.
///
/// C'est le genre de defaut qui ne se voit jamais depuis l'ecran qui le
/// porte : l'interrupteur s'enregistre bien, la page confirme, et rien
/// ne dit que la promesse n'est tenue qu'a moitie.
/// </summary>
[Collection(CollectionApi.Nom)]
public class VivierTests
{
    private readonly ApiEnMemoire _api;

    public VivierTests(ApiEnMemoire api) => _api = api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<JsonElement[]> Chercher(AppUser recruteur, string suite = "")
    {
        var r = await _api.ClientPour(recruteur).GetAsync($"/api/recruiter/candidates/search?{suite}");
        r.EnsureSuccessStatusCode();
        var liste = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);
        return liste.EnumerateArray().ToArray();
    }

    private async Task Regler(AppUser candidat, bool? visible = null, DateTime? disponibleLe = null)
    {
        await _api.DansLaBase(async db =>
        {
            var u = await db.Users.FindAsync(candidat.Id);
            if (visible.HasValue) u!.IsSearchable = visible.Value;
            if (disponibleLe.HasValue) u!.DisponibleLe = disponibleLe.Value;
            return await db.SaveChangesAsync();
        });
    }

    // ══════════════════════════════════════
    //  La promesse de visibilite
    // ══════════════════════════════════════

    [Fact]
    public async Task Un_profil_masque_n_apparait_pas_dans_le_vivier()
    {
        var rec = await _api.Compte("viv-rec", "Recruiter", "Maison A");
        var cache = await _api.Compte("viv-cache", "Candidate");
        await Regler(cache, visible: false);

        var trouves = await Chercher(rec);

        Assert.DoesNotContain(trouves, c => c.GetProperty("id").GetString() == cache.Id);
    }

    [Fact]
    public async Task Un_profil_visible_y_apparait()
    {
        var rec = await _api.Compte("viv-rec2", "Recruiter", "Maison A");
        var visible = await _api.Compte("viv-visible", "Candidate");
        await Regler(visible, visible: true);

        var trouves = await Chercher(rec);

        Assert.Contains(trouves, c => c.GetProperty("id").GetString() == visible.Id);
    }

    // ══════════════════════════════════════
    //  La disponibilite
    // ══════════════════════════════════════

    [Fact]
    public async Task Le_filtre_de_disponibilite_ne_retient_que_ceux_qui_l_ont_declaree()
    {
        var rec = await _api.Compte("viv-dispo-rec", "Recruiter", "Maison A");
        var libre = await _api.Compte("viv-libre", "Candidate");
        var plusTard = await _api.Compte("viv-plus-tard", "Candidate");
        var muet = await _api.Compte("viv-muet", "Candidate");

        await Regler(libre, visible: true, disponibleLe: DateTime.UtcNow.Date.AddDays(-10));
        await Regler(plusTard, visible: true, disponibleLe: DateTime.UtcNow.Date.AddMonths(3));
        await Regler(muet, visible: true);

        var trouves = await Chercher(rec, "disponible=true");
        var ids = trouves.Select(c => c.GetProperty("id").GetString()).ToArray();

        Assert.Contains(libre.Id, ids);
        Assert.DoesNotContain(plusTard.Id, ids);
        // Celui qui n'a rien dit n'est pas « indisponible » : il est
        // simplement hors de cette question. Le filtre l'ecarte sans le
        // juger, et il reste visible sans le filtre.
        Assert.DoesNotContain(muet.Id, ids);

        // …mais il reste visible sans le filtre.
        var sansFiltre = await Chercher(rec);
        Assert.Contains(sansFiltre, c => c.GetProperty("id").GetString() == muet.Id);
    }

    [Fact]
    public async Task La_disponibilite_du_jour_est_dite_au_recruteur()
    {
        var rec = await _api.Compte("viv-dit-rec", "Recruiter", "Maison A");
        var libre = await _api.Compte("viv-dit-libre", "Candidate");
        await Regler(libre, visible: true, disponibleLe: DateTime.UtcNow.Date);

        var fiche = (await Chercher(rec)).First(c => c.GetProperty("id").GetString() == libre.Id);

        Assert.True(fiche.GetProperty("disponibleMaintenant").GetBoolean());
    }

    [Fact]
    public async Task Le_tri_par_disponibilite_met_les_muets_en_fin_de_liste()
    {
        // Les ranger comme une date lointaine fausserait le tri ; les
        // ranger comme aujourd'hui promettrait une disponibilite que
        // personne n'a declaree.
        var rec = await _api.Compte("viv-tri-rec", "Recruiter", "Maison A");
        var libre = await _api.Compte("viv-tri-libre", "Candidate");
        var muet = await _api.Compte("viv-tri-muet", "Candidate");

        await Regler(libre, visible: true, disponibleLe: DateTime.UtcNow.Date.AddDays(-1));
        await Regler(muet, visible: true);

        var ids = (await Chercher(rec, "sort=disponibilite"))
            .Select(c => c.GetProperty("id").GetString()!).ToList();

        Assert.True(ids.IndexOf(libre.Id) < ids.IndexOf(muet.Id));
    }
}
