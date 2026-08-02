using lpdeBack.Models;
using lpdeBack.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace lpdeBack.Tests;

/// <summary>
/// L'empreinte de dedoublonnage et l'analyse de fraude.
///
/// Ces deux mecaniques decident de ce qui entre au catalogue. Une
/// regression sur l'empreinte fait reapparaitre les triplons que le
/// candidat voyait avant ; une regression sur l'analyse laisse passer
/// des annonces qui demandent de l'argent aux candidats.
///
/// Ni l'une ni l'autre ne touche la base : l'empreinte est une fonction
/// pure, et l'analyse ne lit que l'offre qu'on lui donne. Elles se
/// testent donc sans monter d'application.
/// </summary>
public class QualiteCatalogueTests
{
    private static QualiteCatalogue Service() =>
        new(null!, NullLogger<QualiteCatalogue>.Instance);

    // ══════════════════════════════════════
    //  Empreinte
    // ══════════════════════════════════════

    [Fact]
    public void Deux_annonces_identiques_ont_la_meme_empreinte()
    {
        var a = QualiteCatalogue.Empreinte("Développeur web", "Waisso", "Paris");
        var b = QualiteCatalogue.Empreinte("Développeur web", "Waisso", "Paris");
        Assert.Equal(a, b);
    }

    [Theory]
    // La mention de genre est posee par l'agregateur, pas par l'employeur.
    [InlineData("Développeur web H/F")]
    [InlineData("Développeur web (h/f)")]
    [InlineData("DÉVELOPPEUR WEB")]
    [InlineData("Developpeur web")]
    [InlineData("Développeur  web")]
    // L'etiquette de contrat est ajoutee par certaines sources.
    [InlineData("Développeur web CDI")]
    [InlineData("URGENT Développeur web")]
    public void Les_variantes_d_un_meme_intitule_se_rejoignent(string variante)
    {
        var reference = QualiteCatalogue.Empreinte("Développeur web", "Waisso", "Paris");
        Assert.Equal(reference, QualiteCatalogue.Empreinte(variante, "Waisso", "Paris"));
    }

    [Theory]
    // Le lieu se decline d'une source a l'autre : code postal, numero
    // d'arrondissement, parentheses.
    [InlineData("75 - Paris")]
    [InlineData("Paris (75)")]
    [InlineData("Paris 15e")]
    [InlineData("PARIS")]
    public void Les_variantes_d_un_meme_lieu_se_rejoignent(string variante)
    {
        var reference = QualiteCatalogue.Empreinte("Développeur web", "Waisso", "Paris");
        Assert.Equal(reference, QualiteCatalogue.Empreinte("Développeur web", "Waisso", variante));
    }

    [Fact]
    public void Deux_postes_differents_ne_se_confondent_pas()
    {
        var developpeur = QualiteCatalogue.Empreinte("Développeur web", "Waisso", "Paris");

        Assert.NotEqual(developpeur, QualiteCatalogue.Empreinte("Développeur mobile", "Waisso", "Paris"));
        Assert.NotEqual(developpeur, QualiteCatalogue.Empreinte("Développeur web", "Autre SA", "Paris"));
        Assert.NotEqual(developpeur, QualiteCatalogue.Empreinte("Développeur web", "Waisso", "Lyon"));
    }

    [Fact]
    public void Une_empreinte_reste_courte_et_stable()
    {
        // Elle est indexee et comparee des millions de fois a l'import :
        // sa longueur n'est pas un detail.
        var e = QualiteCatalogue.Empreinte("Développeur web", "Waisso", "Paris");
        Assert.Equal(32, e.Length);
    }

    [Fact]
    public void Les_champs_vides_ne_font_pas_lever()
    {
        var e = QualiteCatalogue.Empreinte(null, null, null);
        Assert.Equal(32, e.Length);
    }

    // ══════════════════════════════════════
    //  Analyse de fraude
    // ══════════════════════════════════════

    private static JobOffer Offre(string titre, string description) => new()
    {
        Title = titre,
        Description = description,
        Company = "Waisso",
        Location = "Paris",
    };

    [Fact]
    public void Une_annonce_ordinaire_ne_declenche_rien()
    {
        var (score, motif) = Service().Analyser(Offre(
            "Développeur web",
            "Nous recherchons un développeur pour rejoindre une équipe de six personnes. "
            + "Stack TypeScript et .NET, télétravail deux jours par semaine, mutuelle prise "
            + "en charge à 60 %. Vous participerez aux revues de code et au choix des outils."));

        Assert.Equal(0, score);
        Assert.Null(motif);
    }

    [Theory]
    // Demander de l'argent a un candidat est illegal (L5321-3 du code du travail).
    [InlineData("Frais de dossier de 45 euros à régler avant l'entretien pour valider votre candidature auprès de nos services.")]
    [InlineData("Une caution vous sera demandée pour le matériel fourni au démarrage de votre mission chez notre client.")]
    public void Une_demande_d_argent_franchit_le_seuil(string description)
    {
        var (score, motif) = Service().Analyser(Offre("Assistant administratif", description));

        Assert.True(score >= QualiteCatalogue.SeuilModeration,
            $"Score {score} attendu au-dessus de {QualiteCatalogue.SeuilModeration}");
        Assert.Contains("argent", motif);
    }

    [Fact]
    public void Une_demande_de_piece_d_identite_franchit_le_seuil()
    {
        var (score, _) = Service().Analyser(Offre(
            "Agent logistique",
            "Merci de nous transmettre une copie de votre carte d'identité ainsi que votre RIB "
            + "afin que nous puissions préparer votre dossier avant même le premier entretien."));

        Assert.True(score >= QualiteCatalogue.SeuilModeration);
    }

    [Fact]
    public void Un_contact_sur_messagerie_privee_est_releve()
    {
        var (score, motif) = Service().Analyser(Offre(
            "Chargé de clientèle",
            "Poste à pourvoir immédiatement. Contactez-nous directement sur WhatsApp pour "
            + "organiser un premier échange, nous répondons plus vite par ce canal."));

        Assert.True(score > 0);
        Assert.Contains("messagerie privée".Replace("é", "e"), motif!.Replace("é", "e"));
    }

    [Fact]
    public void Une_description_tres_courte_est_relevee_sans_suffire()
    {
        var (score, _) = Service().Analyser(Offre("Développeur", "Poste à pourvoir."));

        // Un signal faible : beaucoup d'annonces legitimes sont laconiques.
        // Le retenir seul mettrait la moitie du catalogue en moderation.
        Assert.True(score > 0);
        Assert.True(score < QualiteCatalogue.SeuilModeration);
    }

    [Theory]
    // Une annonce par ailleurs irreprochable : longue, bien redigee,
    // salaire plausible, intitule normal. Aucun signal faible ne peut
    // donc venir au secours du signal fort.
    [InlineData("Des frais de dossier de 45 euros vous seront demandés")]
    [InlineData("Merci de joindre une copie de votre carte d'identité")]
    public void Un_signal_quasi_certain_suffit_seul(string phraseSuspecte)
    {
        // Le seuil se franchissait naguere par accumulation : la demande
        // d'argent pesait 50, le seuil valait 60, et il fallait qu'un
        // signal faible — une description courte, par exemple — vienne
        // completer. Une annonce illegale mais bien redigee passait donc.
        // Ce test fige la correction.
        var texte = phraseSuspecte + ". "
            + "Nous recherchons un assistant administratif pour rejoindre une équipe de huit "
            + "personnes au sein de notre agence lyonnaise. Vous assurerez le suivi des dossiers "
            + "clients, la préparation des réunions et la gestion du courrier. Formation assurée "
            + "en interne, mutuelle prise en charge à 60 %, tickets restaurant et treizième mois. "
            + "Une première expérience en assistanat est appréciée sans être exigée.";

        var (score, motif) = Service().Analyser(Offre("Assistant administratif", texte));

        Assert.True(score >= QualiteCatalogue.SeuilModeration,
            $"Score {score} : ce signal doit franchir seul le seuil de {QualiteCatalogue.SeuilModeration}. Motif : {motif}");
    }

    [Fact]
    public void Les_accents_ne_font_pas_echapper_a_l_analyse()
    {
        // Les motifs sont ecrits sans accents, les annonces francaises en
        // portent : « carte d'identité » n'etait pas reconnue, et le
        // signal le plus net du lot passait a travers.
        var accentue = Service().Analyser(Offre("Agent", "Envoyez une copie de votre carte d'identité."));
        var brut = Service().Analyser(Offre("Agent", "Envoyez une copie de votre carte d'identite."));

        Assert.Equal(brut.Score, accentue.Score);
        Assert.True(accentue.Score >= QualiteCatalogue.SeuilModeration);
    }

    [Fact]
    public void L_apostrophe_typographique_ne_fait_pas_echapper_a_l_analyse()
    {
        // Celle des traitements de texte : un copier-coller depuis Word
        // suffisait a rendre le motif inoperant.
        var (score, _) = Service().Analyser(
            Offre("Agent", "Envoyez une copie de votre carte d’identité."));

        Assert.True(score >= QualiteCatalogue.SeuilModeration);
    }

    [Fact]
    public void Filtrer_marque_l_offre_et_la_met_en_moderation()
    {
        var offre = Offre("Assistant",
            "Frais de dossier de 45 euros à régler avant l'entretien. Contactez-nous sur Telegram.");

        var retenue = Service().Filtrer(offre);

        Assert.True(retenue);
        Assert.Equal("Pending", offre.ModerationStatus);
        Assert.NotNull(offre.MotifFraude);
        Assert.True(offre.ScoreFraude >= QualiteCatalogue.SeuilModeration);
    }

    [Fact]
    public void Filtrer_laisse_passer_une_offre_saine_en_notant_son_score()
    {
        var offre = Offre("Développeur web",
            "Équipe de six personnes, stack TypeScript et .NET, télétravail deux jours par "
            + "semaine. Vous participerez aux revues de code et au choix des outils.");
        offre.ModerationStatus = "Approved";

        var retenue = Service().Filtrer(offre);

        Assert.False(retenue);
        Assert.Equal("Approved", offre.ModerationStatus);
        Assert.Equal(0, offre.ScoreFraude);
    }
}
