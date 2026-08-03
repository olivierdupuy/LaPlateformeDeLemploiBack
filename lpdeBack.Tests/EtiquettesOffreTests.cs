using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Les etiquettes que le recruteur pose sur ses offres.
///
/// Du vocabulaire interne — « campagne printemps », « priorite
/// direction », « a revoir » — qui n'a de sens que pour l'equipe qui
/// l'ecrit. D'ou le premier test, et le plus important : elles ne doivent
/// jamais sortir par le catalogue public. « JobOffers.Tags » aurait fait
/// l'affaire techniquement, mais le point d'entree public rend l'entite
/// entiere : une colonne de plus, et « priorite direction » partait chez
/// chaque visiteur.
/// </summary>
[Collection(CollectionApi.Nom)]
public class EtiquettesOffreTests
{
    private readonly ApiEnMemoire _api;

    public EtiquettesOffreTests(ApiEnMemoire api) => _api = api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<HttpResponseMessage> Poser(AppUser qui, int offre, params string[] mots) =>
        await _api.ClientPour(qui).PutAsJsonAsync(
            $"/api/recruiter/offers/{offre}/etiquettes", new { etiquettes = mots });

    private async Task<string[]> Lire(AppUser qui, int offre)
    {
        var r = await _api.ClientPour(qui).GetAsync("/api/recruiter/offers/etiquettes");
        r.EnsureSuccessStatusCode();
        var corps = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);
        var parOffre = corps.GetProperty("parOffre");
        return parOffre.TryGetProperty(offre.ToString(), out var liste)
            ? liste.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
            : Array.Empty<string>();
    }

    // ══════════════════════════════════════
    //  Ce qui ne doit jamais fuir
    // ══════════════════════════════════════

    [Fact]
    public async Task Une_etiquette_interne_ne_sort_pas_par_le_catalogue_public()
    {
        var rec = await _api.Compte("et-fuite", "Recruiter", "Maison Fuite");
        var offre = await _api.Offre(rec.Id);
        await Poser(rec, offre, "priorité direction");

        var r = await _api.ClientAnonyme().GetAsync($"/api/joboffers/{offre}");
        r.EnsureSuccessStatusCode();
        var brut = await r.Content.ReadAsStringAsync();

        Assert.DoesNotContain("priorité direction", brut);
        Assert.DoesNotContain("etiquette", brut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_candidat_ne_lit_pas_les_etiquettes()
    {
        var cand = await _api.Compte("et-cand", "Candidate");

        var r = await _api.ClientPour(cand).GetAsync("/api/recruiter/offers/etiquettes");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    // ══════════════════════════════════════
    //  Poser, remplacer, retirer
    // ══════════════════════════════════════

    [Fact]
    public async Task La_liste_envoyee_fait_foi()
    {
        // Remplacer et non ajouter : l'ecran montre la liste entiere et
        // l'envoie entiere.
        var rec = await _api.Compte("et-remplace", "Recruiter", "Maison A");
        var offre = await _api.Offre(rec.Id);

        await Poser(rec, offre, "campagne", "urgent");
        Assert.Equal(2, (await Lire(rec, offre)).Length);

        await Poser(rec, offre, "campagne");
        var apres = await Lire(rec, offre);

        Assert.Single(apres);
        Assert.Equal("campagne", apres[0]);
    }

    [Fact]
    public async Task Tout_retirer_est_possible()
    {
        var rec = await _api.Compte("et-vide", "Recruiter", "Maison A");
        var offre = await _api.Offre(rec.Id);

        await Poser(rec, offre, "un");
        await Poser(rec, offre);

        Assert.Empty(await Lire(rec, offre));
    }

    [Fact]
    public async Task La_casse_ne_cree_pas_deux_etiquettes()
    {
        // « Urgent » et « urgent » sont la meme : en avoir deux serait une
        // facon de perdre la moitie de ses offres au filtrage.
        var rec = await _api.Compte("et-casse", "Recruiter", "Maison A");
        var offre = await _api.Offre(rec.Id);

        await Poser(rec, offre, "Urgent", "urgent", "  URGENT  ");

        Assert.Single(await Lire(rec, offre));
    }

    [Fact]
    public async Task Les_mots_vides_sont_ignores()
    {
        var rec = await _api.Compte("et-vides", "Recruiter", "Maison A");
        var offre = await _api.Offre(rec.Id);

        await Poser(rec, offre, "utile", "   ", "");

        Assert.Single(await Lire(rec, offre));
    }

    [Fact]
    public async Task Au_dela_de_huit_etiquettes_la_demande_est_refusee()
    {
        var rec = await _api.Compte("et-borne", "Recruiter", "Maison A");
        var offre = await _api.Offre(rec.Id);

        var r = await Poser(rec, offre, Enumerable.Range(1, 9).Select(i => $"mot{i}").ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ══════════════════════════════════════
    //  Le partage d'equipe
    // ══════════════════════════════════════

    [Fact]
    public async Task Une_equipe_partage_ses_etiquettes()
    {
        // Une equipe partage ses offres : elle doit partager la facon de
        // les ranger, sinon chacun reclasse dans son coin.
        var une = await _api.Compte("et-eq-1", "Recruiter", "Maison Commune");
        var autre = await _api.Compte("et-eq-2", "Recruiter", "Maison Commune");
        var offre = await _api.Offre(autre.Id);

        await Poser(autre, offre, "campagne automne");

        Assert.Contains("campagne automne", await Lire(une, offre));
    }

    [Fact]
    public async Task Une_maison_etrangere_ne_voit_ni_ne_pose_rien()
    {
        var mienne = await _api.Compte("et-mienne", "Recruiter", "Maison A");
        var autre = await _api.Compte("et-autre", "Recruiter", "Maison B");
        var offre = await _api.Offre(autre.Id);

        await Poser(autre, offre, "secret de fabrication");

        Assert.Empty(await Lire(mienne, offre));
        Assert.Equal(HttpStatusCode.Forbidden, (await Poser(mienne, offre, "intrus")).StatusCode);
    }

    [Fact]
    public async Task Le_vocabulaire_dedoublonne_les_variantes_de_casse()
    {
        // Sans cela, l'aide a la saisie proposerait « Campagne » et
        // « campagne » comme deux choix distincts, ce qui est exactement
        // la divergence qu'elle doit empecher.
        var rec = await _api.Compte("et-vocab", "Recruiter", "Maison Vocab");
        var a = await _api.Offre(rec.Id);
        var b = await _api.Offre(rec.Id);

        await Poser(rec, a, "Campagne");
        await Poser(rec, b, "campagne");

        var r = await _api.ClientPour(rec).GetAsync("/api/recruiter/offers/etiquettes");
        var corps = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);
        var vocab = corps.GetProperty("vocabulaire").EnumerateArray()
            .Select(x => x.GetString() ?? "")
            .Where(x => string.Equals(x, "campagne", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(vocab);
    }
}
