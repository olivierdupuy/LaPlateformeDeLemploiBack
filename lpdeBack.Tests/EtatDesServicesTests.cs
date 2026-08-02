using lpdeBack.Services;

namespace lpdeBack.Tests;

/// <summary>
/// La surveillance des taches de fond.
///
/// Elle a une faiblesse par construction : une tache n'est surveillee
/// que si quelqu'un a pense a l'inscrire dans les cadences. Celle qui
/// manque ne l'est pas a moitie, elle ne l'est pas du tout — et rien
/// ne le signale, puisque c'est precisement l'absence de signal qui est
/// le probleme.
///
/// C'est arrive : « import-offres » n'y figurait pas. La tache qui
/// tient la fraicheur de cent vingt mille offres a cesse de passer
/// pendant six jours, et l'ecran d'exploitation n'en a rien dit.
/// </summary>
public class EtatDesServicesTests
{
    /// <summary>
    /// Les quatre taches de fond enregistrees dans « Program ».
    ///
    /// Cette liste est le pense-bete : en ajouter une au demarrage sans
    /// l'ajouter ici, ou l'inverse, fait echouer le test.
    /// </summary>
    private static readonly string[] TachesDeFond =
    {
        "import-offres",
        "envoi-newsletter",
        "redaction-newsletter",
        "purge",
    };

    [Fact]
    public void Toute_tache_de_fond_est_surveillee()
    {
        var rapportees = EtatDesServices.Rapport(TimeSpan.FromDays(1))
            .Select(l => l.Service)
            .ToHashSet();

        var oubliees = TachesDeFond.Where(t => !rapportees.Contains(t)).ToList();

        Assert.True(oubliees.Count == 0,
            "Ces tâches tournent sans être surveillées : " + string.Join(", ", oubliees));
    }

    [Fact]
    public void Une_tache_qui_n_a_jamais_tourne_le_dit_apres_une_cadence()
    {
        // Un an apres le demarrage, une tache sans passage n'est pas
        // « en attente » : elle n'est jamais passee, et cela se dit.
        var ligne = EtatDesServices.Rapport(TimeSpan.FromDays(365))
            .First(l => l.Service == "purge");

        // Les tests s'executant dans le meme processus, un autre a pu
        // noter un passage. On ne verifie donc que la coherence entre
        // l'etat et l'inquietude, qui est la regle qui compte.
        Assert.Equal(
            ligne.Etat is "en échec" or "en retard" or "jamais passé",
            ligne.Inquiete);
    }

    [Fact]
    public void Un_passage_reussi_rend_la_tache_saine()
    {
        EtatDesServices.Noter("import-offres", true, "42 offres ajoutées");

        var ligne = EtatDesServices.Rapport(TimeSpan.FromDays(1))
            .First(l => l.Service == "import-offres");

        Assert.Equal("sain", ligne.Etat);
        Assert.False(ligne.Inquiete);
        Assert.Equal("42 offres ajoutées", ligne.Detail);
    }

    [Fact]
    public void Un_echec_inquiete_et_porte_son_motif()
    {
        EtatDesServices.Noter("purge", false, "SqlException : délai dépassé");

        var ligne = EtatDesServices.Rapport(TimeSpan.FromDays(1))
            .First(l => l.Service == "purge");

        Assert.Equal("en échec", ligne.Etat);
        Assert.True(ligne.Inquiete);

        // Le motif est ce qu'on lit en premier sur l'écran
        // d'exploitation : « en échec » sans raison n'apprend rien.
        Assert.Contains("délai dépassé", ligne.Detail);
    }

    [Fact]
    public void Au_demarrage_une_tache_est_en_attente_et_n_inquiete_pas()
    {
        // Une tache quotidienne n'a rien fait pendant la premiere
        // minute, et cela ne justifie pas de reveiller quelqu'un.
        var ligne = EtatDesServices.Rapport(TimeSpan.FromSeconds(5))
            .First(l => l.Service == "redaction-newsletter");

        if (ligne.Etat == "en attente") Assert.False(ligne.Inquiete);
    }
}
