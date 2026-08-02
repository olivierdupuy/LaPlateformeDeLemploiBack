using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Les trois ecrans que la console n'avait pas : integrations, finances,
/// catalogue.
///
/// Ce qui se teste ici n'est pas l'affichage — c'est la portee. Le
/// controleur des integrations existait deja et repondait aux
/// administrateurs, mais sur le compte de l'appelant : un administrateur
/// y voyait ses propres cles, pas celles de la plateforme. Toute la
/// valeur des nouveaux points d'entree tient dans cette difference, et
/// c'est exactement ce qu'une regression effacerait sans bruit.
///
/// Les appels passent par le pipeline complet : un test qui appelle la
/// methode en direct sauterait l'autorisation par role, c'est-a-dire la
/// couche qu'on veut eprouver.
/// </summary>
[Collection(CollectionApi.Nom)]
public class ConsolePlateformeApiTests
{
    private readonly ApiEnMemoire _api;

    public ConsolePlateformeApiTests(ApiEnMemoire api) => _api = api;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<JsonElement> Lire(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(await r.Content.ReadAsStringAsync(), Json);
    }

    // ══════════════════════════════════════
    //  La portee : toute la plateforme, ou le seul appelant
    // ══════════════════════════════════════

    [Fact]
    public async Task Un_administrateur_voit_les_cles_de_tous_les_comptes()
    {
        // Le point du chantier entier : avant, un administrateur
        // n'obtenait que les siennes, et une cle qui fuit ne pouvait etre
        // coupee que par son porteur.
        var admin = await _api.Compte("adm-cles", "Admin");
        var alice = await _api.Compte("rec-alice", "Recruiter", "Alice SARL");
        var bob = await _api.Compte("rec-bob", "Recruiter", "Bob SAS");

        await _api.DansLaBase(async db =>
        {
            db.JetonsApi.Add(new JetonApi
            {
                UserId = alice.Id, Nom = "ats-alice", Prefixe = "lpde_aaa",
                Empreinte = "x", Portees = "offres:lire",
            });
            db.JetonsApi.Add(new JetonApi
            {
                UserId = bob.Id, Nom = "ats-bob", Prefixe = "lpde_bbb",
                Empreinte = "y", Portees = "offres:lire,offres:ecrire",
            });
            return await db.SaveChangesAsync();
        });

        var r = await Lire(await _api.ClientPour(admin).GetAsync("/api/admin/integrations/cles"));
        var noms = r.GetProperty("cles").EnumerateArray()
            .Select(c => c.GetProperty("nom").GetString()).ToList();

        Assert.Contains("ats-alice", noms);
        Assert.Contains("ats-bob", noms);
    }

    [Theory]
    [InlineData("/api/admin/integrations/cles")]
    [InlineData("/api/admin/integrations/webhooks")]
    [InlineData("/api/admin/integrations/diffusions")]
    [InlineData("/api/admin/finances/resume")]
    [InlineData("/api/admin/finances/abonnements")]
    [InlineData("/api/admin/finances/factures")]
    public async Task Un_recruteur_n_atteint_aucun_de_ces_points_d_entree(string chemin)
    {
        // Ces ecrans montrent les cles, les adresses et les factures de
        // tout le monde. Un recruteur qui y accederait verrait le carnet
        // d'adresses de ses concurrents.
        var recruteur = await _api.Compte($"rec-mur-{chemin.GetHashCode():X}", "Recruiter");
        var r = await _api.ClientPour(recruteur).GetAsync(chemin);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Un_visiteur_anonyme_est_refuse()
    {
        var r = await _api.ClientAnonyme().GetAsync("/api/admin/integrations/cles");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ══════════════════════════════════════
    //  Revoquer marque, et ne supprime pas
    // ══════════════════════════════════════

    [Fact]
    public async Task Une_cle_revoquee_reste_en_base()
    {
        // Effacer la ligne rendrait illisibles les journaux du serveur, qui
        // nomment la cle par son prefixe — au moment precis ou l'on cherche
        // a comprendre ce qu'une cle compromise a fait.
        var admin = await _api.Compte("adm-revoc", "Admin");
        var porteur = await _api.Compte("rec-revoc", "Recruiter");

        var id = await _api.DansLaBase(async db =>
        {
            var cle = new JetonApi
            {
                UserId = porteur.Id, Nom = "qui-fuit", Prefixe = "lpde_ccc",
                Empreinte = "z", Portees = "offres:ecrire",
            };
            db.JetonsApi.Add(cle);
            await db.SaveChangesAsync();
            return cle.Id;
        });

        var r = await _api.ClientPour(admin).DeleteAsync($"/api/admin/integrations/cles/{id}");
        r.EnsureSuccessStatusCode();

        var apres = await _api.DansLaBase(async db =>
            await db.JetonsApi.FindAsync(id));

        Assert.NotNull(apres);
        Assert.NotNull(apres!.RevoqueLe);
        Assert.Equal("qui-fuit", apres.Nom);
    }

    [Fact]
    public async Task Revoquer_deux_fois_ne_leve_pas()
    {
        // Deux administrateurs peuvent cliquer en meme temps sur la meme
        // cle. Le second ne doit pas recevoir une erreur pour un travail
        // deja fait.
        var admin = await _api.Compte("adm-revoc2", "Admin");
        var porteur = await _api.Compte("rec-revoc2", "Recruiter");

        var id = await _api.DansLaBase(async db =>
        {
            var cle = new JetonApi { UserId = porteur.Id, Nom = "double", Prefixe = "lpde_ddd", Empreinte = "w" };
            db.JetonsApi.Add(cle);
            await db.SaveChangesAsync();
            return cle.Id;
        });

        var client = _api.ClientPour(admin);
        (await client.DeleteAsync($"/api/admin/integrations/cles/{id}")).EnsureSuccessStatusCode();
        var seconde = await client.DeleteAsync($"/api/admin/integrations/cles/{id}");

        Assert.Equal(HttpStatusCode.OK, seconde.StatusCode);
    }

    // ══════════════════════════════════════
    //  La dormance
    // ══════════════════════════════════════

    [Fact]
    public async Task Une_cle_muette_depuis_trois_mois_est_signalee_dormante()
    {
        var admin = await _api.Compte("adm-dorm", "Admin");
        var porteur = await _api.Compte("rec-dorm", "Recruiter");

        await _api.DansLaBase(async db =>
        {
            db.JetonsApi.Add(new JetonApi
            {
                UserId = porteur.Id, Nom = "endormie", Prefixe = "lpde_eee", Empreinte = "1",
                CreeLe = DateTime.UtcNow.AddYears(-1),
                DerniereUtilisation = DateTime.UtcNow.AddDays(-100),
            });
            db.JetonsApi.Add(new JetonApi
            {
                UserId = porteur.Id, Nom = "vivante", Prefixe = "lpde_fff", Empreinte = "2",
                CreeLe = DateTime.UtcNow.AddYears(-1),
                DerniereUtilisation = DateTime.UtcNow.AddDays(-1),
            });
            return await db.SaveChangesAsync();
        });

        var r = await Lire(await _api.ClientPour(admin).GetAsync("/api/admin/integrations/cles"));
        var cles = r.GetProperty("cles").EnumerateArray().ToList();

        var endormie = cles.First(c => c.GetProperty("nom").GetString() == "endormie");
        var vivante = cles.First(c => c.GetProperty("nom").GetString() == "vivante");

        Assert.True(endormie.GetProperty("dormante").GetBoolean());
        Assert.False(vivante.GetProperty("dormante").GetBoolean());
    }

    [Fact]
    public async Task Une_cle_neuve_jamais_appelee_n_est_pas_dormante()
    {
        // Elle vient d'etre posee : la signaler ferait douter de chaque
        // cle le jour de sa creation.
        var admin = await _api.Compte("adm-neuve", "Admin");
        var porteur = await _api.Compte("rec-neuve", "Recruiter");

        await _api.DansLaBase(async db =>
        {
            db.JetonsApi.Add(new JetonApi
            {
                UserId = porteur.Id, Nom = "toute-neuve", Prefixe = "lpde_ggg", Empreinte = "3",
                CreeLe = DateTime.UtcNow.AddDays(-2),
            });
            return await db.SaveChangesAsync();
        });

        var r = await Lire(await _api.ClientPour(admin).GetAsync("/api/admin/integrations/cles"));
        var neuve = r.GetProperty("cles").EnumerateArray()
            .First(c => c.GetProperty("nom").GetString() == "toute-neuve");

        Assert.False(neuve.GetProperty("dormante").GetBoolean());
        Assert.True(neuve.GetProperty("jamaisUtilisee").GetBoolean());
    }

    // ══════════════════════════════════════
    //  Les finances : rien ne debite personne
    // ══════════════════════════════════════

    [Fact]
    public async Task Une_relance_n_encaisse_rien()
    {
        // La regle qui traverse tout l'ecran. Un prelevement declenche
        // depuis une console serait un debit qu'aucun client n'a autorise
        // ce jour-la, et le journal ne dirait pas pourquoi.
        var admin = await _api.Compte("adm-relance", "Admin");
        var client = await _api.Compte("rec-relance", "Recruiter");

        var id = await _api.DansLaBase(async db =>
        {
            var f = new Facture
            {
                Numero = "F-2026-000001", UserId = client.Id, Libelle = "Formule Essentiel",
                MontantHtCentimes = 4900, TvaCentimes = 980, MontantTtcCentimes = 5880,
                Statut = "emise", EmiseLe = DateTime.UtcNow.AddDays(-40),
            };
            db.Factures.Add(f);
            await db.SaveChangesAsync();
            return f.Id;
        });

        var r = await _api.ClientPour(admin)
            .PostAsync($"/api/admin/finances/factures/{id}/relance", null);
        r.EnsureSuccessStatusCode();

        var apres = await _api.DansLaBase(async db => await db.Factures.FindAsync(id));

        Assert.Equal("emise", apres!.Statut);
        Assert.Null(apres.PayeeLe);
    }

    [Fact]
    public async Task On_ne_relance_pas_une_facture_deja_reglee()
    {
        var admin = await _api.Compte("adm-relance2", "Admin");
        var client = await _api.Compte("rec-relance2", "Recruiter");

        var id = await _api.DansLaBase(async db =>
        {
            var f = new Facture
            {
                Numero = "F-2026-000002", UserId = client.Id, Libelle = "Formule Pro",
                MontantTtcCentimes = 17880, Statut = "payee", PayeeLe = DateTime.UtcNow,
            };
            db.Factures.Add(f);
            await db.SaveChangesAsync();
            return f.Id;
        });

        var r = await _api.ClientPour(admin)
            .PostAsync($"/api/admin/finances/factures/{id}/relance", null);

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
    }

    [Fact]
    public async Task Enregistrer_un_reglement_remet_l_abonnement_en_service()
    {
        // Un virement arrive sur le compte bancaire sans passer par le
        // prestataire. Sans ce geste, la facture reste impayee pour
        // toujours et le client se fait relancer alors qu'il a paye.
        var admin = await _api.Compte("adm-vir", "Admin");
        var client = await _api.Compte("rec-vir", "Recruiter");

        var id = await _api.DansLaBase(async db =>
        {
            db.Abonnements.Add(new Abonnement
            {
                UserId = client.Id, Formule = "essentiel", Statut = "impaye",
            });
            var f = new Facture
            {
                Numero = "F-2026-000003", UserId = client.Id, Libelle = "Formule Essentiel",
                MontantTtcCentimes = 5880, Statut = "emise",
            };
            db.Factures.Add(f);
            await db.SaveChangesAsync();
            return f.Id;
        });

        (await _api.ClientPour(admin)
            .PostAsync($"/api/admin/finances/factures/{id}/payee", null)).EnsureSuccessStatusCode();

        var (statutFacture, statutAbo) = await _api.DansLaBase(async db =>
        {
            var f = await db.Factures.FindAsync(id);
            var a = db.Abonnements.First(x => x.UserId == client.Id);
            return (f!.Statut, a.Statut);
        });

        Assert.Equal("payee", statutFacture);
        Assert.Equal("actif", statutAbo);
    }

    [Fact]
    public async Task Le_resume_compte_les_recettes_encaissees_et_non_les_factures_emises()
    {
        // Une facture emise n'est pas une recette, c'est une esperance.
        // Les confondre donne un chiffre d'affaires qui n'existe pas.
        var admin = await _api.Compte("adm-resume", "Admin");
        var client = await _api.Compte("rec-resume", "Recruiter");

        // En ecart et non en valeur absolue : la base est partagee par
        // toute la collection, et d'autres tests y deposent leurs propres
        // factures. Mesurer le total ferait dependre ce test de l'ordre
        // d'execution — il passerait seul et echouerait en groupe.
        var avant = await Recettes(admin);

        await _api.DansLaBase(async db =>
        {
            db.Factures.Add(new Facture
            {
                Numero = "F-2026-000010", UserId = client.Id, Libelle = "Reglee",
                MontantTtcCentimes = 10_000, MontantHtCentimes = 8_333,
                Statut = "payee", PayeeLe = DateTime.UtcNow,
            });
            db.Factures.Add(new Facture
            {
                Numero = "F-2026-000011", UserId = client.Id, Libelle = "Jamais reglee",
                MontantTtcCentimes = 99_999, Statut = "emise",
            });
            return await db.SaveChangesAsync();
        });

        var apres = await Recettes(admin);

        // La reglee compte, la « jamais reglee » a 99 999 centimes ne
        // compte pas : c'est tout ce que ce test verifie.
        Assert.Equal(10_000, apres - avant);
    }

    private async Task<int> Recettes(AppUser admin)
    {
        var r = await Lire(await _api.ClientPour(admin).GetAsync("/api/admin/finances/resume"));
        return r.GetProperty("recettes").EnumerateArray()
            .Sum(m => m.GetProperty("ttcCentimes").GetInt32());
    }

    // ══════════════════════════════════════
    //  Les deux panneaux d'exploitation
    // ══════════════════════════════════════

    [Theory]
    [InlineData("/api/sante/assistance")]
    [InlineData("/api/sante/requetes-lentes")]
    public async Task Les_releves_d_exploitation_restent_reserves_aux_administrateurs(string chemin)
    {
        // Le releve des requetes lentes expose la forme des requetes, donc
        // celle du schema ; celui de l'assistance expose le nom du modele
        // et la depense. Ni l'un ni l'autre ne regarde un recruteur.
        var recruteur = await _api.Compte($"rec-expl-{chemin.GetHashCode():X}", "Recruiter");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _api.ClientPour(recruteur).GetAsync(chemin)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _api.ClientAnonyme().GetAsync(chemin)).StatusCode);
    }

    [Fact]
    public async Task Le_releve_des_requetes_lentes_annonce_son_seuil()
    {
        // Un releve vide ne veut rien dire tant qu'on ignore a partir de
        // quand une requete y entre.
        var admin = await _api.Compte("adm-lentes", "Admin");
        var r = await Lire(await _api.ClientPour(admin).GetAsync("/api/sante/requetes-lentes"));

        Assert.True(r.GetProperty("seuilMs").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Array, r.GetProperty("formes").ValueKind);
    }

    [Fact]
    public async Task Le_quota_d_assistance_dit_quand_il_repart()
    {
        // Le compteur disparait de lui-meme a minuit UTC : sans cette
        // date, on cherche un bouton de remise a zero qui n'existe pas.
        var admin = await _api.Compte("adm-assist", "Admin");
        var r = await Lire(await _api.ClientPour(admin).GetAsync("/api/sante/assistance"));

        Assert.True(r.GetProperty("plafond").GetInt32() > 0);
        Assert.True(r.GetProperty("remiseAZero").GetDateTime() > DateTime.UtcNow);
    }

    // ══════════════════════════════════════
    //  Le catalogue
    // ══════════════════════════════════════

    [Fact]
    public async Task Le_diagnostic_des_sources_reste_reserve_aux_administrateurs()
    {
        // Il expose l'etat des cles de partenaires : un recruteur n'a rien
        // a y voir.
        var recruteur = await _api.Compte("rec-diag", "Recruiter");
        var r = await _api.ClientPour(recruteur).GetAsync("/api/import/diagnostics");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task La_purge_des_doublons_simule_par_defaut()
    {
        // Supprimer la moitie d'un catalogue ne doit pas tenir a une faute
        // de frappe dans une URL : sans « apply=true », rien n'est ecrit.
        var admin = await _api.Compte("adm-purge", "Admin");
        var auteur = await _api.Compte("rec-purge", "Recruiter");
        await _api.Offre(auteur.Id, titre: "Offre temoin");

        var avant = await _api.DansLaBase(async db => await db.JobOffers.CountAsync());

        var r = await _api.ClientPour(admin).PostAsync("/api/import/duplicates/purge", null);
        r.EnsureSuccessStatusCode();

        var apres = await _api.DansLaBase(async db => await db.JobOffers.CountAsync());
        Assert.Equal(avant, apres);
    }
}
