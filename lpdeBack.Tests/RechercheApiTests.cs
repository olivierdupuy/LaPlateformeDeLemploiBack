using System.Text.Json;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// La recherche en deux temps.
///
/// Chercher dans les descriptions coute dix fois le reste : elles font
/// mille quatre cents caracteres de moyenne, vivent hors page, et aucun
/// index ne peut aider un « LIKE » qui commence par un joker. Le
/// controleur cherche donc d'abord dans le titre, la societe et les
/// etiquettes, et n'ouvre les descriptions que si cette premiere passe ne
/// rend pas de quoi remplir une page.
///
/// Ce que ces tests tiennent, c'est le contrat de ce repli — pas sa
/// vitesse, qui ne se mesure pas sur une base de quelques lignes. Une
/// regression ne se verrait pas autrement : elle ne casserait rien, elle
/// rendrait simplement moins d'offres, ou les memes en dix fois plus de
/// temps.
///
/// Ce qui n'est pas couvert ici, et qu'il faut savoir : l'insensibilite
/// aux accents. Elle est posee par la migration « RechercheSansAccents »
/// sur la collation des colonnes, or ces tests tournent sur SQLite, qui
/// cree son schema depuis le modele sans jouer les migrations et compare
/// les accents en tout etat de cause. Elle est verifiee sur la vraie base
/// SQL Server, pas ici.
/// </summary>
[Collection(CollectionApi.Nom)]
public class RechercheApiTests
{
    private readonly ApiEnMemoire _api;

    public RechercheApiTests(ApiEnMemoire api) => _api = api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Le nombre total annonce par l'en-tete, et les titres rendus.</summary>
    private async Task<(int Total, List<string> Titres)> Chercher(string quoi, string suite = "")
    {
        var r = await _api.ClientAnonyme().GetAsync($"/api/joboffers?search={quoi}{suite}");
        r.EnsureSuccessStatusCode();

        var total = int.Parse(r.Headers.GetValues("X-Total-Count").First());
        var offres = JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);

        return (total, offres.EnumerateArray()
            .Select(o => o.GetProperty("title").GetString() ?? "").ToList());
    }

    /// <summary>
    /// Une offre publiee, dont on choisit ou le mot se trouve. Le mot est
    /// invente : la base est partagee par toute la collection de tests, et
    /// un terme reel y ramasserait les offres des autres.
    /// </summary>
    private async Task Publier(string titre, string description)
    {
        await _api.DansLaBase(async db =>
        {
            db.JobOffers.Add(new JobOffer
            {
                Title = titre,
                Company = "Maison Sans Nom",
                Location = "Perpignan",
                Description = description,
                ContractType = "CDI",
                IsActive = true,
                IsDraft = false,
                ModerationStatus = "Approved",
            });
            return await db.SaveChangesAsync();
        });
    }

    // ══════════════════════════════════════
    //  Le second recours
    // ══════════════════════════════════════

    [Fact]
    public async Task Un_mot_qui_n_est_que_dans_une_description_se_trouve_quand_meme()
    {
        // C'est la raison d'etre du second recours. Sans lui, l'economie
        // se paierait en offres perdues, ce qui n'est pas une economie.
        await Publier("Poste polyvalent", "Nous cherchons une competence en Zorglubine.");

        var (total, _) = await Chercher("Zorglubine");

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Quand_les_titres_remplissent_une_page_les_descriptions_restent_fermees()
    {
        // Vingt-quatre offres portent le mot dans leur titre : le candidat
        // a de quoi lire. La vingt-cinquieme ne le porte que dans sa
        // description — elle sortirait derniere au classement, et elle
        // couterait a elle seule dix fois le reste de la requete.
        for (var i = 1; i <= 24; i++)
            await Publier($"Technicien Wagribole n{i}", "Un poste.");

        await Publier("Agent de maintenance", "Experience en Wagribole appreciee.");

        var (total, _) = await Chercher("Wagribole");

        Assert.Equal(24, total);
    }

    [Fact]
    public async Task Sous_le_seuil_les_descriptions_s_ouvrent()
    {
        // Le pendant du test precedent : trois titres ne font pas une
        // page, donc on va chercher plus loin, et on trouve la quatrieme.
        await Publier("Operateur Kalitrope A", "Un poste.");
        await Publier("Operateur Kalitrope B", "Un poste.");
        await Publier("Operateur Kalitrope C", "Un poste.");
        await Publier("Conducteur de ligne", "Machine Kalitrope sur site.");

        var (total, _) = await Chercher("Kalitrope");

        Assert.Equal(4, total);
    }

    [Fact]
    public async Task Le_total_ne_depend_pas_de_la_taille_de_page_demandee()
    {
        // Le seuil s'ecrivait « page x pageSize » : une meme recherche
        // annoncait alors un nombre d'offres different selon la taille de
        // page, parce qu'en pageSize=1 la premiere passe suffisait
        // toujours a « remplir la page ». Un compteur qui change quand on
        // change de lunette ne compte rien.
        await Publier("Soudeur Brimazol", "Un poste.");
        await Publier("Chaudronnier", "Procede Brimazol maitrise.");

        var (large, _) = await Chercher("Brimazol", "&pageSize=24");
        var (etroit, _) = await Chercher("Brimazol", "&pageSize=1");

        Assert.Equal(2, large);
        Assert.Equal(large, etroit);
    }

    [Fact]
    public async Task Le_repli_vaut_aussi_pour_le_tri_par_date()
    {
        // Le tri par date ne passe pas par le meme chemin : il pagine en
        // SQL au lieu de materialiser puis reclasser. Les deux chemins
        // doivent rendre les memes offres, sans quoi changer le tri
        // changerait le catalogue.
        await Publier("Cariste Nurbex", "Un poste.");
        await Publier("Preparateur de commandes", "Formation Nurbex fournie.");

        var (pertinence, _) = await Chercher("Nurbex");
        var (date, _) = await Chercher("Nurbex", "&sort=date");

        Assert.Equal(2, pertinence);
        Assert.Equal(pertinence, date);
    }

    [Fact]
    public async Task Un_mot_absent_partout_rend_une_liste_vide_et_non_le_catalogue()
    {
        // Le repli elargit la recherche ; il ne doit jamais la lever. Une
        // premiere version qui aurait relache le filtre au lieu de
        // l'elargir aurait rendu les cent mille offres sur une faute de
        // frappe, sans qu'aucun test ne s'en plaigne.
        var (total, titres) = await Chercher("Grumilfaxe");

        Assert.Equal(0, total);
        Assert.Empty(titres);
    }
}
