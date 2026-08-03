using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Les notes que l'equipe laisse sur une candidature.
///
/// « RecruiterNotes » existait deja, mais c'est un champ unique : le
/// second qui ecrit efface le premier, et ni l'un ni l'autre ne s'en
/// apercoit. Deux recruteurs sur le meme dossier se marchaient dessus en
/// silence.
///
/// Le test qui compte est celui du cloisonnement. Un avis ecrit entre
/// collegues — « profil interessant mais pretentions trop hautes » —
/// n'est pas destine a la personne dont on parle, et le suivi de
/// candidature ne doit jamais le laisser passer.
/// </summary>
[Collection(CollectionApi.Nom)]
public class NotesEquipeTests
{
    private readonly ApiEnMemoire _api;

    public NotesEquipeTests(ApiEnMemoire api) => _api = api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<int> Dossier(AppUser recruteur, AppUser candidat)
    {
        var offre = await _api.Offre(recruteur.Id);
        return await _api.DansLaBase(async db =>
        {
            var a = new Application
            {
                JobOfferId = offre, UserId = candidat.Id,
                FullName = "Quelqu'un", Email = "q@exemple.fr",
                Status = StatutCandidature.EnAttente,
            };
            db.Applications.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
    }

    private async Task<HttpResponseMessage> Ecrire(AppUser qui, int dossier, string mot) =>
        await _api.ClientPour(qui).PostAsJsonAsync(
            $"/api/recruiter/applications/{dossier}/notes", new { contenu = mot });

    private async Task<JsonElement[]> Lire(AppUser qui, int dossier)
    {
        var r = await _api.ClientPour(qui).GetAsync($"/api/recruiter/applications/{dossier}/notes");
        r.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json)
            .EnumerateArray().ToArray();
    }

    // ══════════════════════════════════════
    //  Le cloisonnement
    // ══════════════════════════════════════

    [Fact]
    public async Task Le_candidat_ne_voit_jamais_les_notes_de_l_equipe()
    {
        var rec = await _api.Compte("nt-rec", "Recruiter", "Maison A");
        var cand = await _api.Compte("nt-cand", "Candidate");
        var dossier = await Dossier(rec, cand);

        await Ecrire(rec, dossier, "Prétentions trop hautes.");

        var r = await _api.ClientPour(cand).GetAsync("/api/applications/track");
        r.EnsureSuccessStatusCode();
        var brut = await r.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Prétentions trop hautes", brut);
    }

    [Fact]
    public async Task Un_candidat_ne_lit_pas_le_fil_de_notes()
    {
        var rec = await _api.Compte("nt-rec2", "Recruiter", "Maison A");
        var cand = await _api.Compte("nt-cand2", "Candidate");
        var dossier = await Dossier(rec, cand);

        var r = await _api.ClientPour(cand).GetAsync($"/api/recruiter/applications/{dossier}/notes");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Une_maison_etrangere_n_y_accede_pas()
    {
        var rec = await _api.Compte("nt-mienne", "Recruiter", "Maison A");
        var autre = await _api.Compte("nt-autre", "Recruiter", "Maison B");
        var cand = await _api.Compte("nt-cand3", "Candidate");
        var dossier = await Dossier(rec, cand);

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientPour(autre)
            .GetAsync($"/api/recruiter/applications/{dossier}/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Ecrire(autre, dossier, "intrus")).StatusCode);
    }

    // ══════════════════════════════════════
    //  Ce que l'ancien champ ne savait pas faire
    // ══════════════════════════════════════

    [Fact]
    public async Task Deux_collegues_ecrivent_sans_s_effacer()
    {
        // Le defaut d'origine : un champ unique, et le second qui ecrit
        // efface le premier sans que personne ne le voie.
        var une = await _api.Compte("nt-eq-1", "Recruiter", "Maison Commune");
        var autre = await _api.Compte("nt-eq-2", "Recruiter", "Maison Commune");
        var cand = await _api.Compte("nt-eq-cand", "Candidate");
        var dossier = await Dossier(autre, cand);

        await Ecrire(une, dossier, "Vu en visio, bon contact.");
        await Ecrire(autre, dossier, "Références vérifiées.");

        var notes = await Lire(une, dossier);

        Assert.Equal(2, notes.Length);
        Assert.Contains(notes, n => n.GetProperty("contenu").GetString() == "Vu en visio, bon contact.");
        Assert.Contains(notes, n => n.GetProperty("contenu").GetString() == "Références vérifiées.");
    }

    [Fact]
    public async Task Chaque_note_porte_son_auteur()
    {
        var rec = await _api.Compte("nt-auteur", "Recruiter", "Maison A");
        var cand = await _api.Compte("nt-auteur-cand", "Candidate");
        var dossier = await Dossier(rec, cand);

        await Ecrire(rec, dossier, "Un mot.");
        var note = (await Lire(rec, dossier))[0];

        Assert.False(string.IsNullOrWhiteSpace(note.GetProperty("auteurNom").GetString()));
        Assert.True(note.GetProperty("aMoi").GetBoolean());
    }

    [Fact]
    public async Task On_retire_sa_note_et_pas_celle_d_un_collegue()
    {
        var une = await _api.Compte("nt-supp-1", "Recruiter", "Maison Commune");
        var autre = await _api.Compte("nt-supp-2", "Recruiter", "Maison Commune");
        var cand = await _api.Compte("nt-supp-cand", "Candidate");
        var dossier = await Dossier(une, cand);

        await Ecrire(une, dossier, "La mienne.");
        var id = (await Lire(une, dossier))[0].GetProperty("id").GetInt32();

        // Le collegue partage le dossier mais pas la note : effacer le mot
        // d'un autre reecrirait son avis a sa place.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _api.ClientPour(autre).DeleteAsync($"/api/recruiter/applications/{dossier}/notes/{id}")).StatusCode);

        var mienne = await _api.ClientPour(une).DeleteAsync($"/api/recruiter/applications/{dossier}/notes/{id}");
        Assert.True(mienne.IsSuccessStatusCode);
        Assert.Empty(await Lire(une, dossier));
    }

    [Fact]
    public async Task Une_note_vide_est_refusee()
    {
        var rec = await _api.Compte("nt-vide", "Recruiter", "Maison A");
        var cand = await _api.Compte("nt-vide-cand", "Candidate");
        var dossier = await Dossier(rec, cand);

        Assert.Equal(HttpStatusCode.BadRequest, (await Ecrire(rec, dossier, "   ")).StatusCode);
    }
}
