using lpdeBack.Services;

namespace lpdeBack.Tests;

/// <summary>
/// La lecture d'une recherche ecrite en clair.
///
/// C'est le point d'entree du site pour la plupart des visiteurs : ils
/// tapent une phrase, pas des filtres. Avant, cette phrase partait
/// entiere dans un « Title.Contains(...) » et ne trouvait rien —
/// « developpeur react alternance perpignan » ne correspond a aucun
/// intitule d'offre au monde. Le candidat en concluait qu'il n'y avait
/// pas de travail.
///
/// Ces tests figent ce que les regles savent extraire. Aucun n'appelle de
/// modele de langage : c'est precisement ce qu'ils verifient — que la
/// recherche comprend quelque chose sans lui.
/// </summary>
public class RequeteLibreTests
{
    // ══════════════════════════════════════
    //  Le cas qui a motive tout le reste
    // ══════════════════════════════════════

    [Fact]
    public void Une_phrase_entiere_se_decompose_en_filtres_et_en_mots_clefs()
    {
        var r = RequeteLibre.Analyser("developpeur react alternance perpignan");

        // Le nom de famille sert d'identifiant interne ET de libelle
        // affiche : il est donc accentue, comme tout ce qui atteint l'ecran.
        Assert.Equal("Développement", r.Metier);
        Assert.Equal("Alternance", r.Contrat);
        Assert.Equal("Perpignan", r.Lieu);

        // Le metier ne consomme pas ses mots : « developpeur » et
        // « react » restent d'excellents mots-clefs, et les offres dont
        // l'intitule les porte doivent passer devant les autres.
        Assert.Equal("developpeur react", r.Reste);
    }

    // ══════════════════════════════════════
    //  Lieu et rayon
    // ══════════════════════════════════════

    [Theory]
    [InlineData("infirmier a 20 km de Perpignan", 20)]
    [InlineData("infirmier dans un rayon de 30 km de Perpignan", 30)]
    // Sans chiffre, une intention de proximite vaut le rayon par defaut.
    [InlineData("infirmier autour de Perpignan", RequeteLibre.RayonParDefaut)]
    [InlineData("infirmier pres de Perpignan", RequeteLibre.RayonParDefaut)]
    // Une ville nommee sans intention de proximite n'impose pas de rayon.
    [InlineData("infirmier Perpignan", null)]
    public void Le_rayon_se_lit_quand_il_est_annonce(string requete, int? attendu)
    {
        var r = RequeteLibre.Analyser(requete);

        Assert.Equal("Perpignan", r.Lieu);
        Assert.Equal(attendu, r.RayonKm);
    }

    [Fact]
    public void Une_ville_composee_se_reconnait_ecrite_en_un_mot_ou_en_plusieurs()
    {
        Assert.Equal("Aix-en-Provence", RequeteLibre.Analyser("serveur Aix-en-Provence").Lieu);
        Assert.Equal("Aix-en-Provence", RequeteLibre.Analyser("serveur Aix en Provence").Lieu);
    }

    [Fact]
    public void La_ville_reconnue_sort_du_reliquat()
    {
        // Sans cela, « Perpignan » repartait comme mot-clef en plein texte
        // et ne ramenait que les annonces qui le citent dans leur titre,
        // en plus du filtre geographique deja applique.
        var r = RequeteLibre.Analyser("plombier Perpignan");
        Assert.Equal("plombier", r.Reste);
    }

    [Fact]
    public void Un_rayon_sans_ville_est_abandonne()
    {
        // Vingt kilometres autour de rien ne veut rien dire. Mieux vaut
        // ne pas filtrer que filtrer depuis un centre invente.
        var r = RequeteLibre.Analyser("plombier a 20 km");
        Assert.Null(r.Lieu);
        Assert.Null(r.RayonKm);
    }

    // ══════════════════════════════════════
    //  Salaire
    // ══════════════════════════════════════

    [Theory]
    [InlineData("developpeur 45k", 45_000)]
    [InlineData("developpeur a partir de 45k", 45_000)]
    [InlineData("developpeur plus de 45 k€", 45_000)]
    [InlineData("developpeur 45000 euros", 45_000)]
    // « 35 000 » est un seul nombre ecrit a la francaise.
    [InlineData("developpeur 35 000 euros", 35_000)]
    public void Un_salaire_annuel_se_lit_sous_ses_ecritures_courantes(string requete, int attendu)
    {
        Assert.Equal(attendu, RequeteLibre.Analyser(requete).SalaireAnnuelMinimum);
    }

    [Fact]
    public void Un_salaire_mensuel_est_ramene_a_l_annee()
    {
        // Lire « 2500 € par mois » comme un salaire annuel ferait
        // disparaitre toutes les offres correctes du resultat.
        var r = RequeteLibre.Analyser("cariste 2500 euros par mois");
        Assert.Equal(30_000, r.SalaireAnnuelMinimum);
    }

    [Fact]
    public void Un_taux_horaire_est_ramene_a_l_annee()
    {
        var r = RequeteLibre.Analyser("cariste 14 euros de l'heure");
        Assert.Equal(14 * 1607, r.SalaireAnnuelMinimum);
    }

    // ══════════════════════════════════════
    //  Contrat et teletravail
    // ══════════════════════════════════════

    [Theory]
    [InlineData("developpeur en alternance", "Alternance")]
    [InlineData("developpeur en apprentissage", "Alternance")]
    [InlineData("developpeur stage", "Stage")]
    [InlineData("developpeur cdi", "CDI")]
    [InlineData("developpeur interim", "Interim")]
    public void Le_contrat_se_reconnait_sous_ses_synonymes(string requete, string attendu)
    {
        Assert.Equal(attendu, RequeteLibre.Analyser(requete).Contrat);
    }

    [Fact]
    public void Une_alternance_n_est_pas_lue_comme_un_cdd()
    {
        // Un contrat d'apprentissage est juridiquement un CDD, et beaucoup
        // d'annonces portent les deux mentions. Le candidat qui cherche
        // une alternance ne cherche pas un CDD.
        Assert.Equal("Alternance", RequeteLibre.Analyser("alternance cdd 12 mois").Contrat);
    }

    [Theory]
    [InlineData("developpeur teletravail")]
    [InlineData("developpeur en remote")]
    [InlineData("developpeur a distance")]
    public void Le_teletravail_se_reconnait(string requete)
    {
        Assert.True(RequeteLibre.Analyser(requete).Distanciel);
    }

    // ══════════════════════════════════════
    //  Ce qui est rendu a l'appelant
    // ══════════════════════════════════════

    [Fact]
    public void Ce_qui_a_ete_compris_est_dit_en_francais()
    {
        // Une recherche qui applique des filtres que le candidat n'a pas
        // vus passer, et qu'il ne peut donc pas retirer, est une recherche
        // qui ment. L'interface a besoin de ces phrases.
        var r = RequeteLibre.Analyser("infirmier autour de Perpignan en cdi");

        Assert.Contains("à moins de 25 km de Perpignan", r.Compris);
        Assert.Contains("cdi", r.Compris);
        Assert.Contains("métier : santé", r.Compris);
    }

    [Fact]
    public void Une_requete_vide_ne_produit_aucun_filtre()
    {
        var r = RequeteLibre.Analyser("   ");

        Assert.False(r.ADesFiltres);
        Assert.Null(r.Reste);
        Assert.Empty(r.Compris);
    }

    [Fact]
    public void Une_requete_deja_comprise_ne_merite_pas_de_relecture()
    {
        // Le garde-fou de depense : appeler un modele sur « developpeur
        // react perpignan », dont tout a ete extrait, serait de l'argent
        // jete a chaque frappe.
        var r = RequeteLibre.Analyser("developpeur react perpignan");
        Assert.False(r.MeriteUneRelecture);
    }

    [Fact]
    public void Une_phrase_que_les_regles_ne_tiennent_pas_merite_une_relecture()
    {
        var r = RequeteLibre.Analyser(
            "je voudrais accompagner des personnes agees pas trop loin de chez moi");
        Assert.True(r.MeriteUneRelecture);
    }
}
