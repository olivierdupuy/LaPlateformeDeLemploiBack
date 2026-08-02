using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Tests;

/// <summary>
/// Les regles d'argent.
///
/// Elles ont ceci de particulier qu'une erreur ne se voit pas tout de
/// suite : un quota qui laisse passer une offre de trop ne casse rien,
/// un numero de facture saute et personne ne s'en apercoit avant le
/// controle. Ce sont exactement les defauts qu'un test attrape et
/// qu'une relecture manque.
/// </summary>
public class FacturationTests
{
    // ══════════════════════════════════════════
    //  Numerotation
    // ══════════════════════════════════════════

    [Fact]
    public async Task Les_numeros_se_suivent_sans_trou()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var facturation = b.Facturation();

        var numeros = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var f = await facturation.Emettre("u1", "Mise en avant", 2900);
            numeros.Add(f.Numero);
        }

        var annee = DateTime.UtcNow.Year;
        Assert.Equal(
            new[]
            {
                $"F-{annee}-000001", $"F-{annee}-000002", $"F-{annee}-000003",
                $"F-{annee}-000004", $"F-{annee}-000005",
            },
            numeros);
    }

    [Fact]
    public async Task Un_numero_n_est_jamais_reattribue_apres_annulation()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var facturation = b.Facturation();

        var premiere = await facturation.Emettre("u1", "Formule Pro", 14900);
        premiere.Statut = "annulee";
        await b.Contexte.SaveChangesAsync();

        var seconde = await facturation.Emettre("u1", "Formule Pro", 14900);

        // C'est l'obligation comptable : la facture annulee reste, avec
        // son numero, et la suivante prend le suivant. Reutiliser le
        // numero libere ferait deux factures differentes sous la meme
        // reference.
        Assert.NotEqual(premiere.Numero, seconde.Numero);
        Assert.EndsWith("000002", seconde.Numero);
        Assert.Equal(2, b.Contexte.Factures.Count());
    }

    [Fact]
    public async Task Le_numero_porte_l_annee_en_cours()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var f = await b.Facturation().Emettre("u1", "Mise en avant", 2900);

        Assert.StartsWith($"F-{DateTime.UtcNow.Year}-", f.Numero);
    }

    // ══════════════════════════════════════════
    //  TVA
    // ══════════════════════════════════════════

    [Theory]
    [InlineData(2900, 2000, 580, 3480)]    // 29,00 € HT a 20 % → 5,80 € de TVA
    [InlineData(14900, 2000, 2980, 17880)] // la formule Pro
    [InlineData(4900, 2000, 980, 5880)]    // la formule Essentiel
    [InlineData(1000, 0, 0, 1000)]         // exoneration : autoliquidation intracommunautaire
    public async Task La_tva_est_calculee_et_figee(int ht, int taux, int tvaAttendue, int ttcAttendu)
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var f = await b.Facturation().Emettre("u1", "Achat", ht, tauxTvaMillimes: taux);

        Assert.Equal(tvaAttendue, f.TvaCentimes);
        Assert.Equal(ttcAttendu, f.MontantTtcCentimes);
        Assert.Equal(ht + f.TvaCentimes, f.MontantTtcCentimes);
    }

    [Fact]
    public async Task La_tva_arrondit_au_centime_superieur_a_la_demie()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");

        // 1 centime a 20 % vaut 0,2 centime : l'arrondi bancaire donnerait
        // 0, l'arrondi commercial 0 aussi. 3 centimes donnent 0,6 → 1.
        var f = await b.Facturation().Emettre("u1", "Arrondi", 3, tauxTvaMillimes: 2000);

        Assert.Equal(1, f.TvaCentimes);
        Assert.Equal(4, f.MontantTtcCentimes);
    }

    // ══════════════════════════════════════════
    //  Quotas de publication
    // ══════════════════════════════════════════

    [Fact]
    public async Task La_formule_gratuite_s_arrete_a_trois_offres()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var facturation = b.Facturation();

        for (var i = 0; i < 3; i++) b.Contexte.JobOffers.Add(Offre("u1"));
        await b.Contexte.SaveChangesAsync();

        var (autorise, motif, utilisees, quota) = await facturation.PeutPublier("u1");

        Assert.False(autorise);
        Assert.Equal(3, utilisees);
        Assert.Equal(3, quota);
        Assert.Contains("Gratuit", motif);
    }

    [Fact]
    public async Task Les_brouillons_ne_consomment_pas_le_quota()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");

        for (var i = 0; i < 3; i++)
        {
            var brouillon = Offre("u1");
            brouillon.IsDraft = true;
            b.Contexte.JobOffers.Add(brouillon);
        }
        await b.Contexte.SaveChangesAsync();

        var (autorise, _, utilisees, _) = await b.Facturation().PeutPublier("u1");

        // Facturer un brouillon reviendrait a faire payer l'hesitation.
        Assert.True(autorise);
        Assert.Equal(0, utilisees);
    }

    [Fact]
    public async Task Une_offre_fermee_libere_une_place()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");

        for (var i = 0; i < 3; i++) b.Contexte.JobOffers.Add(Offre("u1"));
        await b.Contexte.SaveChangesAsync();

        var fermee = b.Contexte.JobOffers.First();
        fermee.IsActive = false;
        await b.Contexte.SaveChangesAsync();

        var (autorise, _, utilisees, _) = await b.Facturation().PeutPublier("u1");

        Assert.True(autorise);
        Assert.Equal(2, utilisees);
    }

    [Fact]
    public async Task Le_quota_ne_compte_que_les_offres_du_recruteur()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        b.Compte("quelquun-dautre");

        for (var i = 0; i < 3; i++) b.Contexte.JobOffers.Add(Offre("quelquun-dautre"));
        await b.Contexte.SaveChangesAsync();

        var (autorise, _, utilisees, _) = await b.Facturation().PeutPublier("u1");

        Assert.True(autorise);
        Assert.Equal(0, utilisees);
    }

    [Fact]
    public async Task La_formule_pro_n_a_pas_de_plafond()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");

        b.Contexte.Abonnements.Add(new Abonnement { UserId = "u1", Formule = "pro", Statut = "actif" });
        for (var i = 0; i < 40; i++) b.Contexte.JobOffers.Add(Offre("u1"));
        await b.Contexte.SaveChangesAsync();

        var (autorise, _, _, quota) = await b.Facturation().PeutPublier("u1");

        Assert.True(autorise);
        Assert.Equal(-1, quota);
    }

    [Fact]
    public async Task Une_formule_echue_retombe_sur_la_gratuite()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");

        b.Contexte.Abonnements.Add(new Abonnement
        {
            UserId = "u1",
            Formule = "pro",
            Statut = "actif",
            DebutLe = DateTime.UtcNow.AddDays(-60),
            FinLe = DateTime.UtcNow.AddDays(-1),
        });
        await b.Contexte.SaveChangesAsync();

        var formule = await b.Facturation().FormuleDe("u1");

        Assert.Equal("gratuit", formule.Cle);
    }

    [Fact]
    public async Task Une_formule_annulee_ne_compte_plus()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");

        b.Contexte.Abonnements.Add(new Abonnement { UserId = "u1", Formule = "pro", Statut = "annule" });
        await b.Contexte.SaveChangesAsync();

        Assert.Equal("gratuit", (await b.Facturation().FormuleDe("u1")).Cle);
    }

    [Fact]
    public async Task La_formule_retenue_est_la_plus_recente()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");

        b.Contexte.Abonnements.Add(new Abonnement
        { UserId = "u1", Formule = "essentiel", Statut = "actif", DebutLe = DateTime.UtcNow.AddDays(-90) });
        b.Contexte.Abonnements.Add(new Abonnement
        { UserId = "u1", Formule = "pro", Statut = "actif", DebutLe = DateTime.UtcNow.AddDays(-2) });
        await b.Contexte.SaveChangesAsync();

        // Une montee en gamme laisse l'ancienne ligne derriere elle :
        // c'est la derniere souscrite qui fait foi, pas la premiere
        // trouvee.
        Assert.Equal("pro", (await b.Facturation().FormuleDe("u1")).Cle);
    }

    // ══════════════════════════════════════════
    //  Mises en avant
    // ══════════════════════════════════════════

    [Fact]
    public async Task Sans_formule_aucune_mise_en_avant_n_est_incluse()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");

        Assert.Equal(0, await b.Facturation().MisesEnAvantRestantes("u1"));
    }

    [Fact]
    public async Task La_formule_pro_inclut_cinq_mises_en_avant_par_mois()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        b.Contexte.Abonnements.Add(new Abonnement { UserId = "u1", Formule = "pro", Statut = "actif" });
        await b.Contexte.SaveChangesAsync();

        Assert.Equal(5, await b.Facturation().MisesEnAvantRestantes("u1"));
    }

    [Fact]
    public async Task Le_compteur_de_mises_en_avant_repart_au_mois_suivant()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        b.Contexte.Abonnements.Add(new Abonnement { UserId = "u1", Formule = "essentiel", Statut = "actif" });

        // Une mise en avant consommee le mois dernier ne doit pas peser
        // sur celui-ci : le mois est calendaire, pas glissant.
        b.Contexte.MisesEnAvant.Add(new MiseEnAvant
        {
            UserId = "u1",
            JobOfferId = 1,
            Origine = "incluse",
            DebutLe = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-3),
        });
        await b.Contexte.SaveChangesAsync();

        Assert.Equal(1, await b.Facturation().MisesEnAvantRestantes("u1"));
    }

    [Fact]
    public async Task Une_mise_en_avant_payee_ne_consomme_pas_le_quota_inclus()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        b.Contexte.Abonnements.Add(new Abonnement { UserId = "u1", Formule = "essentiel", Statut = "actif" });
        b.Contexte.MisesEnAvant.Add(new MiseEnAvant
        { UserId = "u1", JobOfferId = 1, Origine = "payee", DebutLe = DateTime.UtcNow });
        await b.Contexte.SaveChangesAsync();

        Assert.Equal(1, await b.Facturation().MisesEnAvantRestantes("u1"));
    }

    [Fact]
    public async Task Une_mise_en_avant_incluse_est_gratuite_et_marque_l_offre()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        b.Contexte.Abonnements.Add(new Abonnement { UserId = "u1", Formule = "pro", Statut = "actif" });
        var offre = Offre("u1");
        b.Contexte.JobOffers.Add(offre);
        await b.Contexte.SaveChangesAsync();

        var mise = await b.Facturation().MettreEnAvant("u1", offre.Id, payee: false, reference: null);

        Assert.NotNull(mise);
        Assert.Equal("incluse", mise!.Origine);
        Assert.Equal(0, mise.MontantCentimes);
        Assert.True(b.Contexte.JobOffers.Find(offre.Id)!.IsFeatured);
        Assert.Equal(FacturationService.JoursMiseEnAvant, (mise.FinLe - mise.DebutLe).Days);
    }

    [Fact]
    public async Task Quota_epuise_et_sans_paiement_la_mise_en_avant_est_refusee()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = Offre("u1");
        b.Contexte.JobOffers.Add(offre);
        await b.Contexte.SaveChangesAsync();

        // Formule gratuite : zero mise en avant incluse.
        var mise = await b.Facturation().MettreEnAvant("u1", offre.Id, payee: false, reference: null);

        Assert.Null(mise);
        Assert.False(b.Contexte.JobOffers.Find(offre.Id)!.IsFeatured);
        Assert.Empty(b.Contexte.MisesEnAvant);
    }

    [Fact]
    public async Task Une_mise_en_avant_payee_passe_meme_sans_formule()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = Offre("u1");
        b.Contexte.JobOffers.Add(offre);
        await b.Contexte.SaveChangesAsync();

        var mise = await b.Facturation().MettreEnAvant("u1", offre.Id, payee: true, reference: "pi_123");

        Assert.NotNull(mise);
        Assert.Equal("payee", mise!.Origine);
        Assert.Equal(FacturationService.PrixMiseEnAvantCentimes, mise.MontantCentimes);
        Assert.Equal("pi_123", mise.ReferenceExterne);
    }

    // ══════════════════════════════════════════
    //  Expiration
    // ══════════════════════════════════════════

    [Fact]
    public async Task Une_mise_en_avant_echue_retire_l_etiquette()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = Offre("u1");
        offre.IsFeatured = true;
        b.Contexte.JobOffers.Add(offre);
        await b.Contexte.SaveChangesAsync();

        b.Contexte.MisesEnAvant.Add(new MiseEnAvant
        {
            UserId = "u1",
            JobOfferId = offre.Id,
            DebutLe = DateTime.UtcNow.AddDays(-30),
            FinLe = DateTime.UtcNow.AddDays(-15),
        });
        await b.Contexte.SaveChangesAsync();

        var retirees = await b.Facturation().RetirerLesEchues();

        Assert.Equal(1, retirees);
        Assert.False(b.Contexte.JobOffers.Find(offre.Id)!.IsFeatured);
    }

    [Fact]
    public async Task Une_seconde_mise_en_avant_encore_valable_retient_l_etiquette()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = Offre("u1");
        offre.IsFeatured = true;
        b.Contexte.JobOffers.Add(offre);
        await b.Contexte.SaveChangesAsync();

        b.Contexte.MisesEnAvant.Add(new MiseEnAvant
        {
            UserId = "u1", JobOfferId = offre.Id,
            DebutLe = DateTime.UtcNow.AddDays(-30), FinLe = DateTime.UtcNow.AddDays(-15),
        });
        b.Contexte.MisesEnAvant.Add(new MiseEnAvant
        {
            UserId = "u1", JobOfferId = offre.Id,
            DebutLe = DateTime.UtcNow.AddDays(-1), FinLe = DateTime.UtcNow.AddDays(14),
        });
        await b.Contexte.SaveChangesAsync();

        var retirees = await b.Facturation().RetirerLesEchues();

        // Une offre poussee deux fois ne perd pas sa place parce que la
        // premiere poussee est finie : le client a paye pour la seconde.
        Assert.Equal(0, retirees);
        Assert.True(b.Contexte.JobOffers.Find(offre.Id)!.IsFeatured);
    }

    [Fact]
    public async Task Rien_a_retirer_ne_touche_a_rien()
    {
        using var b = new BaseEnMemoire();
        b.Compte("u1");
        var offre = Offre("u1");
        offre.IsFeatured = true;
        b.Contexte.JobOffers.Add(offre);
        await b.Contexte.SaveChangesAsync();

        b.Contexte.MisesEnAvant.Add(new MiseEnAvant
        {
            UserId = "u1", JobOfferId = offre.Id,
            DebutLe = DateTime.UtcNow, FinLe = DateTime.UtcNow.AddDays(15),
        });
        await b.Contexte.SaveChangesAsync();

        Assert.Equal(0, await b.Facturation().RetirerLesEchues());
        Assert.True(b.Contexte.JobOffers.Find(offre.Id)!.IsFeatured);
    }

    // ══════════════════════════════════════════

    private static JobOffer Offre(string proprietaire) => new()
    {
        Title = "Developpeur",
        Company = "TechCorp",
        Location = "Marseille",
        Description = "Un poste.",
        CreatedByUserId = proprietaire,
        IsActive = true,
        IsDraft = false,
    };
}
