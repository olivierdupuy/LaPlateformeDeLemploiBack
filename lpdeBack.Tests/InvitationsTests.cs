using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Inviter un profil du vivier a postuler.
///
/// Le vivier permettait de trouver quelqu'un et de le regarder. Pour lui
/// parler, il fallait passer par la messagerie, hors de toute offre : le
/// candidat recevait « votre profil m'interesse » sans savoir pour quel
/// poste, et rien ne rattachait l'echange a un recrutement.
///
/// L'invitation reste une PROPOSITION. Tout ce qui suit protege cette
/// idee : on n'invite pas deux fois, on n'invite pas quelqu'un qui s'est
/// masque, on n'invite pas sur une annonce hors ligne, et un silence
/// n'est jamais compte comme un refus.
/// </summary>
[Collection(CollectionApi.Nom)]
public class InvitationsTests
{
    private readonly ApiEnMemoire _api;

    public InvitationsTests(ApiEnMemoire api) => _api = api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<AppUser> Candidat(string graine, bool visible = true)
    {
        var c = await _api.Compte(graine, "Candidate");
        await _api.DansLaBase(async db =>
        {
            var u = await db.Users.FindAsync(c.Id);
            u!.IsSearchable = visible;
            return await db.SaveChangesAsync();
        });
        return c;
    }

    private async Task<HttpResponseMessage> Inviter(AppUser rec, int offre, string candidatId, string? mot = null) =>
        await _api.ClientPour(rec).PostAsJsonAsync("/api/recruiter/invitations",
            new { jobOfferId = offre, candidatId, message = mot });

    private async Task<JsonElement[]> Recues(AppUser candidat)
    {
        var r = await _api.ClientPour(candidat).GetAsync("/api/candidate/invitations");
        r.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json)
            .EnumerateArray().ToArray();
    }

    // ══════════════════════════════════════
    //  Le trajet nominal
    // ══════════════════════════════════════

    [Fact]
    public async Task Le_candidat_recoit_l_invitation_et_sait_pour_quel_poste()
    {
        var rec = await _api.Compte("inv-rec", "Recruiter", "Maison A");
        var cand = await Candidat("inv-cand");
        var offre = await _api.Offre(rec.Id);

        (await Inviter(rec, offre, cand.Id, "Votre profil correspond.")).EnsureSuccessStatusCode();

        var recues = await Recues(cand);
        Assert.Single(recues);
        Assert.Equal(offre, recues[0].GetProperty("jobOfferId").GetInt32());
        Assert.Equal("Votre profil correspond.", recues[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Le_candidat_est_prevenu()
    {
        var rec = await _api.Compte("inv-notif-rec", "Recruiter", "Maison A");
        var cand = await Candidat("inv-notif-cand");
        var offre = await _api.Offre(rec.Id);

        await Inviter(rec, offre, cand.Id);

        var combien = await _api.DansLaBase(async db => await Task.FromResult(
            db.Notifications.Count(n => n.UserId == cand.Id && n.Type == "Invitation")));
        Assert.Equal(1, combien);
    }

    [Fact]
    public async Task La_lecture_vaut_accuse()
    {
        // Le recruteur doit pouvoir distinguer « pas encore regarde » de
        // « regarde et laisse sans suite » : sans cela il relance
        // quelqu'un qui n'a simplement pas ouvert la page.
        var rec = await _api.Compte("inv-vue-rec", "Recruiter", "Maison A");
        var cand = await Candidat("inv-vue-cand");
        var offre = await _api.Offre(rec.Id);
        await Inviter(rec, offre, cand.Id);

        await Recues(cand);

        var vue = await _api.DansLaBase(async db => await Task.FromResult(
            db.Invitations.First(i => i.CandidatId == cand.Id && i.JobOfferId == offre).VueLe));
        Assert.NotNull(vue);
    }

    [Fact]
    public async Task Decliner_solde_l_invitation()
    {
        var rec = await _api.Compte("inv-decl-rec", "Recruiter", "Maison A");
        var cand = await Candidat("inv-decl-cand");
        var offre = await _api.Offre(rec.Id);
        await Inviter(rec, offre, cand.Id);

        var id = (await Recues(cand))[0].GetProperty("id").GetInt32();
        var r = await _api.ClientPour(cand).PatchAsync($"/api/candidate/invitations/{id}/decliner", null);
        r.EnsureSuccessStatusCode();

        var reponse = await _api.DansLaBase(async db => await Task.FromResult(
            db.Invitations.First(i => i.Id == id).Reponse));
        Assert.Equal(Invitation.Declinee, reponse);
    }

    // ══════════════════════════════════════
    //  Ce qu'on refuse d'envoyer
    // ══════════════════════════════════════

    [Fact]
    public async Task On_n_invite_pas_deux_fois_sur_la_meme_offre()
    {
        // Reinviter quelqu'un sur la meme annonce est du harcelement poli,
        // pas une relance.
        var rec = await _api.Compte("inv-double-rec", "Recruiter", "Maison A");
        var cand = await Candidat("inv-double-cand");
        var offre = await _api.Offre(rec.Id);

        (await Inviter(rec, offre, cand.Id)).EnsureSuccessStatusCode();
        var seconde = await Inviter(rec, offre, cand.Id);

        Assert.Equal(HttpStatusCode.BadRequest, seconde.StatusCode);
    }

    [Fact]
    public async Task On_n_invite_pas_un_profil_masque()
    {
        // Le vivier respecte « IsSearchable » ; l'invitation doit le
        // respecter aussi, sans quoi un identifiant devine suffirait a
        // contourner le masquage.
        var rec = await _api.Compte("inv-masque-rec", "Recruiter", "Maison A");
        var cache = await Candidat("inv-masque-cand", visible: false);
        var offre = await _api.Offre(rec.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await Inviter(rec, offre, cache.Id)).StatusCode);
    }

    [Fact]
    public async Task On_n_invite_pas_sur_une_offre_hors_ligne()
    {
        // Le candidat arriverait sur une page vide.
        var rec = await _api.Compte("inv-horsligne-rec", "Recruiter", "Maison A");
        var cand = await Candidat("inv-horsligne-cand");
        var offre = await _api.Offre(rec.Id, active: false);

        Assert.Equal(HttpStatusCode.BadRequest, (await Inviter(rec, offre, cand.Id)).StatusCode);
    }

    [Fact]
    public async Task Un_recruteur_n_invite_pas_sur_l_offre_d_une_autre_maison()
    {
        var mienne = await _api.Compte("inv-mienne", "Recruiter", "Maison A");
        var autre = await _api.Compte("inv-autre", "Recruiter", "Maison B");
        var cand = await Candidat("inv-autre-cand");
        var offre = await _api.Offre(autre.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await Inviter(mienne, offre, cand.Id)).StatusCode);
    }

    [Fact]
    public async Task Un_candidat_ne_voit_que_ses_invitations()
    {
        var rec = await _api.Compte("inv-cloison-rec", "Recruiter", "Maison A");
        var un = await Candidat("inv-cloison-1");
        var deux = await Candidat("inv-cloison-2");
        var offre = await _api.Offre(rec.Id);

        await Inviter(rec, offre, un.Id);

        Assert.Empty(await Recues(deux));
    }

    [Fact]
    public async Task Un_candidat_ne_decline_pas_l_invitation_d_un_autre()
    {
        var rec = await _api.Compte("inv-decl2-rec", "Recruiter", "Maison A");
        var un = await Candidat("inv-decl2-1");
        var deux = await Candidat("inv-decl2-2");
        var offre = await _api.Offre(rec.Id);
        await Inviter(rec, offre, un.Id);

        var id = (await Recues(un))[0].GetProperty("id").GetInt32();
        var r = await _api.ClientPour(deux).PatchAsync($"/api/candidate/invitations/{id}/decliner", null);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ══════════════════════════════════════
    //  Le suivi cote recruteur
    // ══════════════════════════════════════

    [Fact]
    public async Task L_equipe_voit_les_invitations_de_ses_offres()
    {
        var une = await _api.Compte("inv-eq-1", "Recruiter", "Maison Commune");
        var autre = await _api.Compte("inv-eq-2", "Recruiter", "Maison Commune");
        var cand = await Candidat("inv-eq-cand");
        var offre = await _api.Offre(autre.Id);

        await Inviter(autre, offre, cand.Id);

        var r = await _api.ClientPour(une).GetAsync("/api/recruiter/invitations");
        r.EnsureSuccessStatusCode();
        var liste = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);

        Assert.Contains(liste.EnumerateArray(), i => i.GetProperty("jobOfferId").GetInt32() == offre);
    }
}
