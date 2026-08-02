using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// Les frontieres qui font mal.
///
/// Ce ne sont pas des tests de fonctionnalite : aucun ne verifie qu'une
/// offre s'affiche correctement. Ils verifient qu'on ne peut pas faire
/// ce qu'on n'a pas le droit de faire — et c'est la seule categorie de
/// defaut dont le cout ne se mesure pas en gene mais en incident.
///
/// Chacun d'eux passe par le vrai pipeline HTTP : jeton signe, filtres,
/// autorisation. Un test qui appelle la methode du controleur en direct
/// aurait rapporte « vert » sur les cinq failles ci-dessous.
/// </summary>
public class FrontieresApiTests : IClassFixture<ApiEnMemoire>
{
    private readonly ApiEnMemoire _api;

    public FrontieresApiTests(ApiEnMemoire api) => _api = api;

    // ══════════════════════════════════════════
    //  Autorisation par role
    // ══════════════════════════════════════════

    [Fact]
    public async Task Un_visiteur_anonyme_n_atteint_pas_la_console_recruteur()
    {
        var reponse = await _api.ClientAnonyme().GetAsync("/api/joboffers/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_candidat_n_atteint_pas_la_console_recruteur()
    {
        var candidat = await _api.Compte("role-candidat", "Candidate");

        var reponse = await _api.ClientPour(candidat).GetAsync("/api/joboffers/mine");

        // 403 et non 401 : le jeton est valable, c'est le role qui ne
        // l'est pas. La distinction compte — un 401 ferait retenter une
        // connexion qui ne changerait rien.
        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_recruteur_n_atteint_pas_les_statistiques_d_administration()
    {
        var recruteur = await _api.Compte("role-recruteur-stats", "Recruiter");

        var reponse = await _api.ClientPour(recruteur).GetAsync("/api/joboffers/stats/admin/apercu");

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_recruteur_atteint_bien_sa_propre_console()
    {
        var recruteur = await _api.Compte("role-recruteur-ok", "Recruiter");

        var reponse = await _api.ClientPour(recruteur).GetAsync("/api/joboffers/mine");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    // ══════════════════════════════════════════
    //  Perimetre recruteur
    // ══════════════════════════════════════════

    [Fact]
    public async Task Un_recruteur_ne_touche_pas_a_l_offre_d_une_autre_entreprise()
    {
        var chezEux = await _api.Compte("perim-chez-eux", "Recruiter", entreprise: "TechCorp");
        var chezNous = await _api.Compte("perim-chez-nous", "Recruiter", entreprise: "AutreBoite");
        var offre = await _api.Offre(chezEux.Id);

        var reponse = await _api.ClientPour(chezNous).PatchAsync($"/api/joboffers/{offre}/feature", null);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_collegue_de_la_meme_entreprise_gere_l_offre()
    {
        var auteur = await _api.Compte("perim-auteur", "Recruiter", entreprise: "MemeBoite");
        var collegue = await _api.Compte("perim-collegue", "Recruiter", entreprise: "MemeBoite");
        var offre = await _api.Offre(auteur.Id);

        var reponse = await _api.ClientPour(collegue).PatchAsync($"/api/joboffers/{offre}/feature", null);

        // C'est tout l'objet du perimetre : un recruteur qui part ne doit
        // pas emporter les offres de l'entreprise avec lui.
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Sans_entreprise_declaree_on_ne_gere_que_ses_propres_offres()
    {
        var seul = await _api.Compte("perim-seul-a", "Recruiter");
        var autreSeul = await _api.Compte("perim-seul-b", "Recruiter");
        var offre = await _api.Offre(seul.Id);

        var reponse = await _api.ClientPour(autreSeul).PatchAsync($"/api/joboffers/{offre}/feature", null);

        // Sans ce cas, tous les comptes sans entreprise formeraient une
        // equipe commune — exactement l'inverse du but.
        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_administrateur_passe_le_perimetre()
    {
        var recruteur = await _api.Compte("perim-admin-cible", "Recruiter", entreprise: "TechCorp");
        var admin = await _api.Compte("perim-admin", "Admin");
        var offre = await _api.Offre(recruteur.Id);

        var reponse = await _api.ClientPour(admin).PatchAsync($"/api/joboffers/{offre}/feature", null);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    // ══════════════════════════════════════════
    //  Route authentifiee des CV
    // ══════════════════════════════════════════

    [Fact]
    public async Task Un_CV_ne_se_telecharge_pas_sans_jeton()
    {
        var reponse = await _api.ClientAnonyme().GetAsync("/api/fichiers/cv/quelconque_20260412161541.pdf");

        // Le point capital : les CV etaient servis en statique, donc
        // lisibles de quiconque devinait le nom. Ce sont des donnees
        // personnelles au sens du RGPD.
        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_nom_de_fichier_qui_remonte_l_arborescence_est_refuse()
    {
        var candidat = await _api.Compte("cv-traversee", "Candidate");

        var reponse = await _api.ClientPour(candidat)
            .GetAsync("/api/fichiers/cv/..%2F..%2Fappsettings.json");

        Assert.True(
            reponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
                or HttpStatusCode.Forbidden,
            $"Une traversee de repertoire a recu {(int)reponse.StatusCode}.");
    }

    // ══════════════════════════════════════════
    //  Brouillons
    // ══════════════════════════════════════════

    [Fact]
    public async Task On_ne_postule_pas_a_un_brouillon()
    {
        var recruteur = await _api.Compte("brouillon-recruteur", "Recruiter");
        var candidat = await _api.Compte("brouillon-candidat", "Candidate");
        var brouillon = await _api.Offre(recruteur.Id, brouillon: true);

        var reponse = await _api.ClientPour(candidat).PostAsJsonAsync("/api/applications", new
        {
            jobOfferId = brouillon,
            fullName = "Camille Martin",
            email = "camille@exemple.fr",
            coverLetter = "Bonjour.",
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);

        var candidatures = await _api.DansLaBase(db =>
            db.Applications.CountAsync(a => a.JobOfferId == brouillon));
        Assert.Equal(0, candidatures);
    }

    [Fact]
    public async Task Un_brouillon_jamais_publie_ne_se_renouvelle_pas()
    {
        var recruteur = await _api.Compte("brouillon-renouv", "Recruiter");
        var brouillon = await _api.Offre(recruteur.Id, brouillon: true);

        var reponse = await _api.ClientPour(recruteur)
            .PatchAsync($"/api/joboffers/{brouillon}/renew", null);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_recruteur_ne_postule_pas()
    {
        var recruteur = await _api.Compte("postule-recruteur", "Recruiter");
        var offre = await _api.Offre(recruteur.Id);

        var reponse = await _api.ClientPour(recruteur).PostAsJsonAsync("/api/applications", new
        {
            jobOfferId = offre,
            fullName = "Recruteur Curieux",
            email = "rec@exemple.fr",
        });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    // ══════════════════════════════════════════
    //  Quota de formule
    // ══════════════════════════════════════════

    [Fact]
    public async Task La_quatrieme_offre_est_refusee_en_formule_gratuite()
    {
        var recruteur = await _api.Compte("quota-gratuit", "Recruiter");
        for (var i = 0; i < 3; i++) await _api.Offre(recruteur.Id, titre: $"Poste {i}");

        var reponse = await _api.ClientPour(recruteur).PostAsJsonAsync("/api/joboffers", new
        {
            title = "La quatrieme",
            company = "TechCorp",
            location = "Marseille",
            description = "Un poste de plus, decrit assez longuement pour passer la validation.",
            contractType = "CDI",
            category = "Tech",
        });

        // 402 « paiement requis », et non 400 : ce n'est ni une erreur de
        // saisie ni un droit manquant. Le client sait a ce code qu'il
        // faut proposer la page de facturation plutot qu'un message
        // d'erreur sur un champ.
        Assert.Equal(HttpStatusCode.PaymentRequired, reponse.StatusCode);
        var corps = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("formule", corps, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task La_formule_pro_ne_bute_pas_sur_le_quota()
    {
        var recruteur = await _api.Compte("quota-pro", "Recruiter");
        await _api.DansLaBase(async db =>
        {
            db.Abonnements.Add(new Abonnement
            { UserId = recruteur.Id, Formule = "pro", Statut = "actif" });
            return await db.SaveChangesAsync();
        });
        for (var i = 0; i < 5; i++) await _api.Offre(recruteur.Id, titre: $"Pro {i}");

        var reponse = await _api.ClientPour(recruteur).PostAsJsonAsync("/api/joboffers", new
        {
            title = "La sixieme",
            company = "TechCorp",
            location = "Marseille",
            description = "Un poste de plus, decrit assez longuement pour passer la validation.",
            contractType = "CDI",
            category = "Tech",
        });

        Assert.True(
            reponse.IsSuccessStatusCode,
            $"La formule Pro est illimitee, et l'API a repondu {(int)reponse.StatusCode} : "
            + await reponse.Content.ReadAsStringAsync());
    }
}
