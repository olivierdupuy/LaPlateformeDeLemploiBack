using System.Net.Http.Json;
using System.Text.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Les actions groupees, et ce qu'elles rapportent.
///
/// Deux defauts se cachaient dans celle qui existait deja. Elle comparait
/// l'auteur de l'offre a l'appelant au lieu de passer par le perimetre
/// d'equipe : un recruteur pouvait traiter la candidature d'un collegue
/// une par une, et pas en lot — la ligne etait simplement ignoree, sans
/// un mot. Et son compte rendu portait le nombre de candidatures LUES,
/// pas modifiees : selectionner douze dossiers dont trois vous reviennent
/// affichait « 12 mises a jour », et personne n'allait verifier les neuf
/// autres.
///
/// Un compte rendu faux est pire qu'une action qui echoue : l'echec se
/// voit, le chiffre faux se croit.
/// </summary>
[Collection(CollectionApi.Nom)]
public class ActionsGroupeesTests
{
    private readonly ApiEnMemoire _api;

    public ActionsGroupeesTests(ApiEnMemoire api) => _api = api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<JsonElement> Lire(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);
    }

    private async Task<int> Candidature(int offre, string candidat)
    {
        return await _api.DansLaBase(async db =>
        {
            var a = new Application
            {
                JobOfferId = offre, UserId = candidat,
                FullName = "Quelqu'un", Email = "q@exemple.fr",
                Status = StatutCandidature.EnAttente,
            };
            db.Applications.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
    }

    // ══════════════════════════════════════
    //  Le compte rendu
    // ══════════════════════════════════════

    [Fact]
    public async Task Le_compte_rendu_ne_porte_que_ce_qui_a_ete_modifie()
    {
        // Une candidature a moi, une a une maison etrangere. Le serveur en
        // lit deux et n'en modifie qu'une : c'est « un » qu'il doit dire.
        var moi = await _api.Compte("ag-moi", "Recruiter", "Maison A");
        var etranger = await _api.Compte("ag-etranger", "Recruiter", "Maison B");
        var candidat = await _api.Compte("ag-cand", "Candidate");

        var mienne = await Candidature(await _api.Offre(moi.Id), candidat.Id);
        var sienne = await Candidature(await _api.Offre(etranger.Id), candidat.Id);

        var corps = await Lire(await _api.ClientPour(moi).PatchAsJsonAsync(
            "/api/recruiter/applications/bulk-status",
            new { ids = new[] { mienne, sienne }, status = StatutCandidature.Examinee }));

        Assert.Equal(1, corps.GetProperty("updated").GetInt32());
        Assert.Equal(2, corps.GetProperty("demandees").GetInt32());
    }

    [Fact]
    public async Task Une_candidature_d_une_autre_maison_n_est_pas_touchee()
    {
        var moi = await _api.Compte("ag-moi2", "Recruiter", "Maison A");
        var etranger = await _api.Compte("ag-etranger2", "Recruiter", "Maison B");
        var candidat = await _api.Compte("ag-cand2", "Candidate");
        var sienne = await Candidature(await _api.Offre(etranger.Id), candidat.Id);

        await _api.ClientPour(moi).PatchAsJsonAsync(
            "/api/recruiter/applications/bulk-status",
            new { ids = new[] { sienne }, status = StatutCandidature.Refusee });

        var statut = await _api.DansLaBase(async db =>
            (await db.Applications.FindAsync(sienne))!.Status);
        Assert.Equal(StatutCandidature.EnAttente, statut);
    }

    [Fact]
    public async Task Un_collegue_de_la_meme_maison_peut_traiter_en_lot()
    {
        // Le defaut corrige : le partage d'equipe valait a l'unite et pas
        // en lot. Deux recruteurs, une meme entreprise.
        var une = await _api.Compte("ag-equipe-1", "Recruiter", "Maison Commune");
        var autre = await _api.Compte("ag-equipe-2", "Recruiter", "Maison Commune");
        // Depuis les roles d'equipe, traiter le dossier d'un collegue
        // demande d'etre proprietaire : la meme maison ne suffit plus.
        await _api.DansLaBase(async db =>
        {
            var u = await db.Users.FindAsync(une.Id);
            u!.RoleEquipe = RolesEquipe.Proprietaire;
            return await db.SaveChangesAsync();
        });
        var candidat = await _api.Compte("ag-equipe-c", "Candidate");
        var dossier = await Candidature(await _api.Offre(autre.Id), candidat.Id);

        var corps = await Lire(await _api.ClientPour(une).PatchAsJsonAsync(
            "/api/recruiter/applications/bulk-status",
            new { ids = new[] { dossier }, status = StatutCandidature.Contactee }));

        Assert.Equal(1, corps.GetProperty("updated").GetInt32());
    }

    [Fact]
    public async Task Traiter_en_lot_horodate_la_consultation()
    {
        var rec = await _api.Compte("ag-horodate", "Recruiter", "Maison A");
        var candidat = await _api.Compte("ag-horodate-c", "Candidate");
        var dossier = await Candidature(await _api.Offre(rec.Id), candidat.Id);

        await _api.ClientPour(rec).PatchAsJsonAsync(
            "/api/recruiter/applications/bulk-status",
            new { ids = new[] { dossier }, status = StatutCandidature.Examinee });

        var lu = await _api.DansLaBase(async db =>
            (await db.Applications.FindAsync(dossier))!.ReviewedAt);
        Assert.NotNull(lu);
    }

    // ══════════════════════════════════════
    //  Les offres
    // ══════════════════════════════════════

    [Fact]
    public async Task Suspendre_plusieurs_offres_d_un_geste()
    {
        var rec = await _api.Compte("ag-offres", "Recruiter", "Maison A");
        var a = await _api.Offre(rec.Id);
        var b = await _api.Offre(rec.Id);

        var corps = await Lire(await _api.ClientPour(rec).PatchAsJsonAsync(
            "/api/recruiter/offers/bulk-etat",
            new { ids = new[] { a, b }, etat = EtatOffre.Suspendue }));

        Assert.Equal(2, corps.GetProperty("updated").GetInt32());

        var visibles = await _api.DansLaBase(async db =>
            await Task.FromResult(db.JobOffers.Count(o => (o.Id == a || o.Id == b) && o.IsActive)));
        Assert.Equal(0, visibles);
    }

    [Fact]
    public async Task Les_brouillons_sont_ecartes_et_comptes_a_part()
    {
        // Silencieusement sautes, ils feraient croire a un lot traite en
        // entier. Le compte rendu les nomme.
        var rec = await _api.Compte("ag-brouillons", "Recruiter", "Maison A");
        var publiee = await _api.Offre(rec.Id);
        var brouillon = await _api.Offre(rec.Id, brouillon: true);

        var corps = await Lire(await _api.ClientPour(rec).PatchAsJsonAsync(
            "/api/recruiter/offers/bulk-etat",
            new { ids = new[] { publiee, brouillon }, etat = EtatOffre.Fermee }));

        Assert.Equal(1, corps.GetProperty("updated").GetInt32());
        Assert.Equal(1, corps.GetProperty("ignorees").GetInt32());
    }

    [Fact]
    public async Task Le_lot_d_offres_est_borne()
    {
        var rec = await _api.Compte("ag-borne", "Recruiter", "Maison A");

        var r = await _api.ClientPour(rec).PatchAsJsonAsync(
            "/api/recruiter/offers/bulk-etat",
            new { ids = Enumerable.Range(1, 201).ToArray(), etat = EtatOffre.Fermee });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, r.StatusCode);
    }
}
