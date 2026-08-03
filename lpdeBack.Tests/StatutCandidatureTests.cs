using System.Net;
using System.Net.Http.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Le parcours d'une candidature, elargi de quatre etats a six.
///
/// La liste des valeurs admises etait recopiee dans quatre controleurs et
/// deux attributs de validation. Six endroits a tenir d'accord a la main :
/// le premier oubli n'aurait rien casse de visible, il aurait seulement
/// fait accepter un statut par un chemin et refuser par un autre. Ces
/// tests passent par le pipeline complet, donc par les attributs de
/// validation — appeler la methode en direct les sauterait, c'est-a-dire
/// precisement la couche qu'on veut eprouver.
/// </summary>
[Collection(CollectionApi.Nom)]
public class StatutCandidatureTests
{
    private readonly ApiEnMemoire _api;

    public StatutCandidatureTests(ApiEnMemoire api) => _api = api;

    /// <summary>Un recruteur, son offre, et une candidature dessus.</summary>
    private async Task<(AppUser Recruteur, int Candidature)> Dossier(string graine)
    {
        var recruteur = await _api.Compte($"stt-rec-{graine}", "Recruiter", $"Societe {graine}");
        var candidat = await _api.Compte($"stt-cand-{graine}", "Candidate");
        var offre = await _api.Offre(recruteur.Id);

        var id = await _api.DansLaBase(async db =>
        {
            var a = new Application
            {
                JobOfferId = offre, UserId = candidat.Id,
                FullName = "Candidat d'essai", Email = $"{graine}@exemple.fr",
                Status = StatutCandidature.EnAttente,
            };
            db.Applications.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });

        return (recruteur, id);
    }

    private async Task<HttpResponseMessage> Poser(AppUser recruteur, int candidature, string statut) =>
        await _api.ClientPour(recruteur)
            .PatchAsJsonAsync($"/api/applications/{candidature}/status", new { status = statut });

    // ══════════════════════════════════════
    //  Les six etats
    // ══════════════════════════════════════

    [Theory]
    [InlineData("Pending")]
    [InlineData("Reviewed")]
    [InlineData("Contacted")]
    [InlineData("Accepted")]
    [InlineData("Hired")]
    [InlineData("Rejected")]
    public async Task Les_six_etats_du_parcours_sont_acceptes(string statut)
    {
        var (recruteur, candidature) = await Dossier($"ok-{statut}");

        var r = await Poser(recruteur, candidature, statut);

        Assert.True(r.IsSuccessStatusCode, $"« {statut} » a ete refuse : {r.StatusCode}");
    }

    [Fact]
    public async Task La_liste_centrale_en_compte_exactement_six()
    {
        // Le document de suivi a annonce quatre etats, puis cinq — en
        // prenant « Interview » pour un statut alors que c'est le nom
        // d'une entite du journal d'audit. Ce test fige le compte.
        Assert.Equal(6, StatutCandidature.Tous.Length);
        Assert.Equal(StatutCandidature.Tous.Length, StatutCandidature.Tous.Distinct().Count());
    }

    [Fact]
    public async Task Un_etat_invente_est_refuse()
    {
        var (recruteur, candidature) = await Dossier("inconnu");

        var r = await Poser(recruteur, candidature, "Shortlisted");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ══════════════════════════════════════
    //  Ce que l'etat entraine
    // ══════════════════════════════════════

    [Fact]
    public async Task Passer_directement_a_contactee_horodate_la_consultation()
    {
        // Un recruteur qui ecrit tout de suite a forcement lu le dossier.
        // Sans cela, le candidat voyait l'etape « Consultee » eteinte
        // alors qu'on venait de le contacter.
        var (recruteur, candidature) = await Dossier("horodatage");

        await Poser(recruteur, candidature, StatutCandidature.Contactee);

        var lu = await _api.DansLaBase(async db =>
            (await db.Applications.FindAsync(candidature))!.ReviewedAt);

        Assert.NotNull(lu);
    }

    [Fact]
    public async Task Rester_en_attente_n_horodate_rien()
    {
        var (recruteur, candidature) = await Dossier("pas-horodate");

        await Poser(recruteur, candidature, StatutCandidature.EnAttente);

        var lu = await _api.DansLaBase(async db =>
            (await db.Applications.FindAsync(candidature))!.ReviewedAt);

        Assert.Null(lu);
    }

    [Fact]
    public async Task La_premiere_consultation_ne_se_reecrit_pas()
    {
        // « Consultee il y a trois jours » doit rester la date du premier
        // regard, pas celle du dernier changement d'etat.
        var (recruteur, candidature) = await Dossier("premiere-fois");

        await Poser(recruteur, candidature, StatutCandidature.Examinee);
        var premiere = await _api.DansLaBase(async db =>
            (await db.Applications.FindAsync(candidature))!.ReviewedAt);

        await Poser(recruteur, candidature, StatutCandidature.Acceptee);
        var apres = await _api.DansLaBase(async db =>
            (await db.Applications.FindAsync(candidature))!.ReviewedAt);

        Assert.Equal(premiere, apres);
    }

    // ══════════════════════════════════════
    //  Ce qui reste en jeu
    // ══════════════════════════════════════

    [Fact]
    public void Une_embauche_et_un_refus_n_attendent_plus_rien()
    {
        // « EnCours » decide qui compte parmi les dossiers qui dorment et
        // meritent une relance. Y laisser un dossier clos ferait relancer
        // quelqu'un qu'on a deja embauche.
        Assert.DoesNotContain(StatutCandidature.Embauchee, StatutCandidature.EnCours);
        Assert.DoesNotContain(StatutCandidature.Refusee, StatutCandidature.EnCours);
        Assert.Contains(StatutCandidature.Contactee, StatutCandidature.EnCours);

        Assert.True(StatutCandidature.EstTermine(StatutCandidature.Embauchee));
        Assert.False(StatutCandidature.EstTermine(StatutCandidature.Contactee));
    }

    [Fact]
    public void Chaque_etat_a_un_libelle_francais()
    {
        // Un etat sans libelle s'affiche en anglais au candidat.
        foreach (var s in StatutCandidature.Tous)
            Assert.NotEqual(s, StatutCandidature.Libelle(s));
    }
}
