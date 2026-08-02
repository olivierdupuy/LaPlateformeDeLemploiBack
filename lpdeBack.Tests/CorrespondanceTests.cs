using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Tests;

/// <summary>
/// Le rapprochement d'un candidat et d'une offre.
///
/// Des pondérations rouillent en silence : personne ne remarque qu'un
/// critere ne se declenche plus, on constate seulement, six mois plus
/// tard, que les recommandations sont devenues quelconques. Ces tests
/// figent le comportement, y compris les trois defauts de la version
/// precedente qui ont motive sa reecriture.
///
/// Aucun n'appelle de modele de langage — c'est le point : la
/// correspondance doit tenir sans cle d'API.
/// </summary>
public class CorrespondanceTests
{
    // ── De quoi fabriquer des cas lisibles ──

    private static AppUser Candidat(
        string? titre = "Developpeur web",
        string? competences = "React, TypeScript, SQL",
        string? ville = "Perpignan",
        int? annees = 5,
        string? formation = null) =>
        new()
        {
            Title = titre,
            Skills = competences,
            City = ville,
            ExperienceYears = annees,
            Education = formation,
        };

    private static JobOffer Offre(
        string titre = "Developpeur front-end",
        string? etiquettes = "react, typescript",
        string categorie = "Informatique",
        string lieu = "Perpignan",
        double? lat = null,
        double? lng = null,
        string? experience = null,
        string? formationExigee = null,
        bool distanciel = false,
        string contrat = "CDI",
        int? salaireMax = null,
        string? periode = null) =>
        new()
        {
            Title = titre,
            Tags = etiquettes,
            Category = categorie,
            Location = lieu,
            Latitude = lat,
            Longitude = lng,
            ExperienceRequired = experience,
            EducationLevel = formationExigee,
            IsRemote = distanciel,
            ContractType = contrat,
            MaxSalary = salaireMax,
            SalaryPeriod = periode,
            Description = "Description suffisamment longue pour ne rien declencher.",
        };

    // ══════════════════════════════════════
    //  Les poids
    // ══════════════════════════════════════

    [Fact]
    public void La_somme_des_poids_fait_cent()
    {
        // Sans quoi la fiabilite ne veut plus rien dire : elle se calcule
        // comme la part du total effectivement jugee.
        var total = Correspondance.PoidsMetier + Correspondance.PoidsCompetences
                    + Correspondance.PoidsLieu + Correspondance.PoidsContrat
                    + Correspondance.PoidsExperience + Correspondance.PoidsFormation
                    + Correspondance.PoidsSalaire;

        Assert.Equal(100, total);
    }

    // ══════════════════════════════════════
    //  Les trois defauts de la version precedente
    // ══════════════════════════════════════

    [Fact]
    public void Un_profil_detaille_n_est_pas_penalise()
    {
        // L'ancien calcul divisait par le nombre de competences du
        // candidat : celui qui en saisissait vingt n'atteignait jamais le
        // seuil, celui qui en saisissait deux le depassait toujours. Le
        // site punissait le soin.
        var offre = Offre(etiquettes: "react, typescript, sql");

        var sobre = Correspondance.Noter(Candidat(competences: "React, TypeScript, SQL"), offre);
        var bavard = Correspondance.Noter(
            Candidat(competences: "React, TypeScript, SQL, Docker, Git, Figma, Python, "
                                  + "Java, PHP, Angular, Vue, Node, AWS, Linux, Agile, Scrum"),
            offre);

        Assert.Equal(sobre.Score, bavard.Score);
    }

    [Fact]
    public void Un_profil_sans_competences_saisies_obtient_quand_meme_une_note()
    {
        // L'ancien calcul renvoyait un tableau vide — pas un mauvais
        // score, rien. C'est le cas de la majorite des inscrits.
        var note = Correspondance.Noter(
            Candidat(competences: null, annees: null), Offre());

        Assert.True(note.Score > 0);
        Assert.NotEmpty(note.Raisons);
    }

    [Fact]
    public void La_distance_se_calcule_au_lieu_de_comparer_des_libelles()
    {
        // « Location.Contains(City) » ignorait Canet-en-Roussillon a onze
        // kilometres de Perpignan. Le candidat n'y voyait aucune offre.
        var canet = Offre(lieu: "Canet-en-Roussillon", lat: 42.7044, lng: 3.0325);
        var note = Correspondance.Noter(Candidat(ville: "Perpignan"), canet);

        Assert.Contains(note.Raisons, r => r.Contains("km"));
        Assert.True(note.Score >= 70, $"score obtenu : {note.Score}");
    }

    [Fact]
    public void Un_poste_a_l_autre_bout_du_pays_passe_derriere()
    {
        var ici = Correspondance.Noter(Candidat(), Offre(lieu: "Perpignan"));
        var loin = Correspondance.Noter(Candidat(), Offre(lieu: "Lille"));

        Assert.True(loin.Score < ici.Score);
        Assert.Contains(loin.Reserves, r => r.Contains("km"));
    }

    // ══════════════════════════════════════
    //  Metier
    // ══════════════════════════════════════

    [Fact]
    public void Un_autre_metier_coute_cher_et_se_dit()
    {
        var note = Correspondance.Noter(
            Candidat(titre: "Infirmier", competences: null),
            Offre(titre: "Developpeur front-end"));

        Assert.Contains(note.Reserves, r => r.Contains("autre métier"));
        Assert.True(note.Score < 50, $"score obtenu : {note.Score}");
    }

    [Fact]
    public void Un_metier_inconnu_du_lexique_ne_penalise_personne()
    {
        // Le lexique a des angles morts. Repondre « zero » pour un metier
        // qu'il ignore reviendrait a punir le candidat de notre lacune :
        // le critere est retire du calcul, pas mis a zero.
        var note = Correspondance.Noter(
            Candidat(titre: "Souffleur de verre", competences: null, ville: "Perpignan"),
            Offre(titre: "Souffleur de verre", etiquettes: null, categorie: "Artisanat"));

        Assert.DoesNotContain(note.Reserves, r => r.Contains("autre métier"));
    }

    // ══════════════════════════════════════
    //  Criteres inconnus
    // ══════════════════════════════════════

    [Fact]
    public void Le_silence_d_une_offre_ne_se_paie_pas_comme_un_echec()
    {
        // Une annonce qui ne dit pas l'experience attendue ne doit pas
        // passer derriere une annonce qui l'exige hors de portee. Le
        // critere absent sort du calcul ; il n'est pas compte a zero.
        var muette = Correspondance.Noter(Candidat(annees: 5), Offre());
        var horsDePortee = Correspondance.Noter(
            Candidat(annees: 5), Offre(experience: "Expert"));

        Assert.True(muette.Score > horsDePortee.Score,
            $"muette : {muette.Score}, hors de portee : {horsDePortee.Score}");
    }

    [Fact]
    public void Plus_l_offre_est_precise_plus_le_score_engage()
    {
        // Le score reste un pourcentage des criteres jugeables. Ce qui
        // change avec le detail de l'annonce, c'est la part de l'analyse
        // reellement menee — et c'est elle qui permet a l'affichage de
        // dire « correspondance estimee » plutot qu'un chiffre peremptoire.
        var muette = Correspondance.Noter(Candidat(), Offre());
        var detaillee = Correspondance.Noter(
            Candidat(annees: 5, formation: "Master informatique"),
            Offre(experience: "Senior", formationExigee: "Bac+5"));

        Assert.True(detaillee.Fiabilite > muette.Fiabilite,
            $"muette : {muette.Fiabilite}, detaillee : {detaillee.Fiabilite}");
    }

    [Fact]
    public void Un_score_etabli_sur_trop_peu_de_criteres_s_annonce_comme_tel()
    {
        var note = Correspondance.Noter(
            Candidat(titre: null, competences: null, annees: null),
            Offre(etiquettes: null, categorie: ""));

        Assert.True(note.Fiabilite < Correspondance.FiabiliteMinimale,
            $"fiabilite obtenue : {note.Fiabilite}");
    }

    [Fact]
    public void Un_candidat_dont_on_ne_sait_rien_obtient_zero_et_aucune_raison()
    {
        var note = Correspondance.Noter(
            Candidat(titre: null, competences: null, ville: null, annees: null),
            Offre(etiquettes: null, categorie: ""));

        Assert.Equal(0, note.Score);
        Assert.Equal(0, note.Fiabilite);
        Assert.Empty(note.Raisons);
    }

    // ══════════════════════════════════════
    //  Souhaits
    // ══════════════════════════════════════

    [Fact]
    public void Le_contrat_ne_compte_que_si_le_candidat_en_a_exprime_un()
    {
        // « AppUser » ne porte pas le contrat vise : sans souhait connu,
        // le critere n'existe pas plutot que d'etre suppose.
        var sans = Correspondance.Noter(Candidat(), Offre(contrat: "Stage"));
        var avec = Correspondance.Noter(Candidat(), Offre(contrat: "Stage"),
            new Souhaits(Contrat: "CDI"));

        Assert.True(avec.Score < sans.Score);
        Assert.Contains(avec.Reserves, r => r.Contains("cdi"));
    }

    [Fact]
    public void Un_salaire_horaire_se_compare_a_un_souhait_annuel()
    {
        // « MaxSalary » est un entier dont l'unite vit dans un autre
        // champ. Comparer 14 a 25 000 sans lire « SalaryPeriod » ferait
        // passer un poste correct pour une misere.
        var note = Correspondance.Noter(
            Candidat(),
            Offre(salaireMax: 14, periode: "heure"),
            new Souhaits(SalaireAnnuelMinimum: 20_000));

        Assert.Contains(note.Raisons, r => r.Contains("par an"));
    }

    [Fact]
    public void Le_rayon_fixe_par_le_candidat_tranche()
    {
        var note = Correspondance.Noter(
            Candidat(ville: "Perpignan"),
            Offre(lieu: "Montpellier"),
            new Souhaits(RayonKm: 30));

        Assert.Contains(note.Reserves, r => r.Contains("rayon"));
    }

    // ══════════════════════════════════════
    //  Experience
    // ══════════════════════════════════════

    [Theory]
    [InlineData("Junior", 0)]
    [InlineData("Senior", 5)]
    [InlineData("3 ans", 3)]
    public void L_experience_exigee_se_lit_sous_ses_ecritures(string libelle, int plancher)
    {
        // Juste au plancher : le critere est satisfait, sans reserve.
        var pile = Correspondance.Noter(
            Candidat(annees: plancher), Offre(experience: libelle));
        Assert.DoesNotContain(pile.Reserves, r => r.Contains("expérience"));

        // Un cran en dessous : la reserve doit apparaitre.
        if (plancher > 0)
        {
            var court = Correspondance.Noter(
                Candidat(annees: plancher - 1), Offre(experience: libelle));
            Assert.Contains(court.Reserves, r => r.Contains("expérience"));
        }
    }

    [Fact]
    public void La_surqualification_se_signale_sans_couter_de_points()
    {
        // Ce n'est pas au site de decider qu'un poste est « en dessous »
        // de quelqu'un. On le dit, chacun en fait ce qu'il veut.
        var note = Correspondance.Noter(
            Candidat(annees: 15), Offre(experience: "Junior"));

        Assert.Contains(note.Reserves, r => r.Contains("junior"));
        Assert.True(note.Score >= 80, $"score obtenu : {note.Score}");
    }

    // ══════════════════════════════════════
    //  Ce que les phrases disent, et a qui
    // ══════════════════════════════════════

    [Fact]
    public void Sans_souhaits_connus_aucune_phrase_ne_tutoie_ni_ne_vouvoie()
    {
        // Les memes phrases sont rendues au candidat et au recruteur qui
        // classe ses candidatures. Celles qui dependent des souhaits du
        // candidat s'adressent a lui — mais un recruteur ne les connait
        // pas, donc elles ne se declenchent jamais chez lui. Toutes les
        // autres doivent se lire des deux cotes.
        var note = Correspondance.Noter(
            Candidat(annees: 2, formation: "BTS"),
            Offre(lieu: "Lille", experience: "Senior", formationExigee: "Bac+5"));

        foreach (var phrase in note.Raisons.Concat(note.Reserves))
        {
            Assert.DoesNotContain("vous", phrase, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("votre", phrase, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Un_bon_rapprochement_dit_toujours_pourquoi()
    {
        // Un score nu n'autorise personne a decider quoi que ce soit.
        var note = Correspondance.Noter(Candidat(), Offre());

        Assert.True(note.Score >= 70);
        Assert.NotEmpty(note.Raisons);
    }

    [Fact]
    public void Le_teletravail_efface_la_distance()
    {
        var note = Correspondance.Noter(
            Candidat(ville: "Perpignan"),
            Offre(lieu: "Lille", distanciel: true));

        Assert.Contains(note.Raisons, r => r.Contains("télétravail"));
        Assert.DoesNotContain(note.Reserves, r => r.Contains("km"));
    }
}
