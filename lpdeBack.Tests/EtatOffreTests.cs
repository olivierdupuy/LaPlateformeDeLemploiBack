using System.Net;
using System.Net.Http.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Ouvrir, suspendre, fermer une offre.
///
/// Le seul geste disponible etait la suppression, qui emporte les
/// candidatures deja recues. Un recruteur qui voulait mettre une annonce
/// en pause le temps d'un arbitrage n'avait donc le choix qu'entre perdre
/// ses dossiers et continuer d'en recevoir.
///
/// Ce qui se teste ici n'est pas le libelle des trois etats : c'est
/// l'invariant. « IsActive » reste la seule condition des requetes
/// publiques, et il doit valoir vrai si et seulement si l'etat est
/// « ouverte ». Une divergence ne casserait rien de visible — elle
/// laisserait une offre annoncee fermee au recruteur et toujours servie
/// au public, ou l'inverse.
/// </summary>
[Collection(CollectionApi.Nom)]
public class EtatOffreTests
{
    private readonly ApiEnMemoire _api;

    public EtatOffreTests(ApiEnMemoire api) => _api = api;

    private async Task<HttpResponseMessage> Poser(AppUser qui, int offre, string etat) =>
        await _api.ClientPour(qui).PatchAsJsonAsync($"/api/joboffers/{offre}/etat", new { etat });

    private async Task<(string Etat, bool Visible)> Lire(int offre) =>
        await _api.DansLaBase(async db =>
        {
            var o = await db.JobOffers.FindAsync(offre);
            return (o!.EtatPublication, o.IsActive);
        });

    // ══════════════════════════════════════
    //  L'invariant
    // ══════════════════════════════════════

    [Theory]
    [InlineData("ouverte", true)]
    [InlineData("suspendue", false)]
    [InlineData("fermee", false)]
    public async Task La_visibilite_suit_l_etat(string etat, bool visibleAttendu)
    {
        var rec = await _api.Compte($"eo-{etat}", "Recruiter", "Maison Test");
        var offre = await _api.Offre(rec.Id);

        var r = await Poser(rec, offre, etat);
        r.EnsureSuccessStatusCode();

        var (lu, visible) = await Lire(offre);
        Assert.Equal(etat, lu);
        Assert.Equal(visibleAttendu, visible);
    }

    [Fact]
    public async Task Une_offre_suspendue_puis_rouverte_redevient_visible()
    {
        // La suspension doit etre reversible sans passer par la
        // suppression : c'est toute sa raison d'etre.
        var rec = await _api.Compte("eo-aller-retour", "Recruiter", "Maison Test");
        var offre = await _api.Offre(rec.Id);

        await Poser(rec, offre, EtatOffre.Suspendue);
        Assert.False((await Lire(offre)).Visible);

        await Poser(rec, offre, EtatOffre.Ouverte);
        Assert.True((await Lire(offre)).Visible);
    }

    [Fact]
    public async Task Suspendre_une_offre_ne_touche_pas_a_ses_candidatures()
    {
        // C'est ce que la suppression coutait, et la raison d'etre de ce
        // lot : mettre une annonce en pause ne doit rien effacer.
        var rec = await _api.Compte("eo-dossiers", "Recruiter", "Maison Test");
        var cand = await _api.Compte("eo-dossiers-c", "Candidate");
        var offre = await _api.Offre(rec.Id);

        await _api.DansLaBase(async db =>
        {
            db.Applications.Add(new Application
            {
                JobOfferId = offre, UserId = cand.Id,
                FullName = "Quelqu'un", Email = "q@exemple.fr",
                Status = StatutCandidature.EnAttente,
            });
            return await db.SaveChangesAsync();
        });

        await Poser(rec, offre, EtatOffre.Suspendue);

        var combien = await _api.DansLaBase(async db =>
            await Task.FromResult(db.Applications.Count(a => a.JobOfferId == offre)));
        Assert.Equal(1, combien);
    }

    // ══════════════════════════════════════
    //  Les refus
    // ══════════════════════════════════════

    [Fact]
    public async Task Un_etat_invente_est_refuse()
    {
        var rec = await _api.Compte("eo-invente", "Recruiter", "Maison Test");
        var offre = await _api.Offre(rec.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await Poser(rec, offre, "archivee")).StatusCode);
    }

    [Fact]
    public async Task Un_brouillon_n_a_pas_d_etat_de_publication()
    {
        // Il n'a jamais ete publie : le suspendre n'a pas de sens, et le
        // rouvrir d'ici sauterait la moderation.
        var rec = await _api.Compte("eo-brouillon", "Recruiter", "Maison Test");
        var offre = await _api.Offre(rec.Id, brouillon: true);

        Assert.Equal(HttpStatusCode.BadRequest, (await Poser(rec, offre, EtatOffre.Suspendue)).StatusCode);
    }

    [Fact]
    public async Task Une_offre_en_attente_de_moderation_ne_se_rouvre_pas_d_ici()
    {
        // Sans ce garde-fou, le point d'entree serait un contournement de
        // la moderation : suspendre puis rouvrir remettrait en ligne une
        // annonce que personne n'a relue.
        var rec = await _api.Compte("eo-moderation", "Recruiter", "Maison Test");
        var offre = await _api.Offre(rec.Id);

        await _api.DansLaBase(async db =>
        {
            var o = await db.JobOffers.FindAsync(offre);
            o!.ModerationStatus = "Pending";
            return await db.SaveChangesAsync();
        });

        var r = await Poser(rec, offre, EtatOffre.Ouverte);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Un_recruteur_ne_touche_pas_a_l_offre_d_une_autre_maison()
    {
        var mienne = await _api.Compte("eo-mienne", "Recruiter", "Maison A");
        var autre = await _api.Compte("eo-autre", "Recruiter", "Maison B");
        var offre = await _api.Offre(autre.Id);

        var r = await Poser(mienne, offre, EtatOffre.Fermee);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Un_candidat_ne_change_l_etat_d_aucune_offre()
    {
        var rec = await _api.Compte("eo-rec-garde", "Recruiter", "Maison Test");
        var cand = await _api.Compte("eo-cand-garde", "Candidate");
        var offre = await _api.Offre(rec.Id);

        var r = await Poser(cand, offre, EtatOffre.Fermee);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
