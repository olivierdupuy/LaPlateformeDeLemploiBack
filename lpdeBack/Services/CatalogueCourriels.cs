namespace lpdeBack.Services;

/// <summary>
/// Les modeles transactionnels, vus depuis l'administration.
///
/// Quatorze messages partent au nom de la plateforme, et personne ne
/// les avait jamais lus autrement qu'en declenchant la situation qui
/// les produit. Relire le courriel de suppression de compte supposait
/// de supprimer un compte ; relire la decision d'un signalement DSA,
/// d'instruire un signalement pour de vrai. Autant dire qu'on les
/// relisait apres coup, dans la boite du destinataire.
///
/// Ce catalogue les rend tous avec des donnees d'exemple. Deux usages,
/// et ils ne se confondent pas :
///
///   — l'apercu, qui ne part nulle part et sert a relire une tournure
///     ou verifier qu'un lien pointe au bon endroit ;
///   — l'essai, qui expedie le modele choisi a une adresse donnee, pour
///     voir ce que le client de messagerie en fait vraiment. Un gabarit
///     correct dans le navigateur peut s'effondrer dans Outlook.
///
/// Les donnees d'exemple sont volontairement reconnaissables — « Camille
/// Martin », « TechCorp » — pour qu'un essai recu ne puisse jamais etre
/// confondu avec un vrai message.
/// </summary>
public static class CatalogueCourriels
{
    /// <param name="Cle">Identifiant stable, utilise par l'interface.</param>
    /// <param name="Nom">Ce que l'administrateur lit dans la liste.</param>
    /// <param name="Quand">La situation qui declenche l'envoi.</param>
    /// <param name="Categorie">« Securite », « Compte », « Candidatures »…</param>
    public record Modele(string Cle, string Nom, string Quand, string Categorie);

    private const string LienExemple = "https://laplateformedelemploi.com/exemple?jeton=EXEMPLE";

    /// <summary>Tous les modeles, dans l'ordre ou on les rencontre.</summary>
    public static readonly Modele[] Tous =
    {
        new("confirmation", "Confirmation d'adresse",
            "A l'inscription, avant que le compte ne serve.", "Compte"),
        new("reinitialisation", "Reinitialisation du mot de passe",
            "Sur demande de « mot de passe oublie ».", "Securite"),
        new("mot-de-passe-change", "Mot de passe modifie",
            "Apres coup, comme alarme.", "Securite"),
        new("nouvelle-connexion", "Connexion depuis un appareil inconnu",
            "A chaque connexion depuis un appareil jamais vu.", "Securite"),
        new("double-authentification", "Double authentification activee",
            "Quand le second facteur est active ou coupe.", "Securite"),
        new("compte-inactif", "Compte inactif bientot ferme",
            "Avant la purge RGPD des comptes dormants.", "Compte"),
        new("compte-efface", "Compte efface",
            "Apres l'effacement, confirme ce qui a disparu.", "Compte"),
        new("candidature-recue", "Candidature bien recue",
            "Au candidat, des l'envoi de sa candidature.", "Candidatures"),
        new("nouvelle-candidature", "Nouvelle candidature",
            "Au recruteur, quand quelqu'un postule.", "Candidatures"),
        new("statut-candidature", "Changement de statut",
            "Au candidat, quand le recruteur fait avancer son dossier.", "Candidatures"),
        new("confirmation-newsletter", "Confirmation d'abonnement",
            "Au double opt-in de la lettre d'information.", "Lettre"),
        new("accuse-signalement", "Accuse de signalement",
            "Au declarant, des reception de son signalement DSA.", "Conformite"),
        new("decision-signalement", "Decision motivee",
            "Au declarant, quand le signalement est instruit.", "Conformite"),
        new("essai", "Message de controle",
            "Uniquement depuis cet ecran.", "Exploitation"),
    };

    /// <summary>
    /// Rend un modele avec des donnees d'exemple, ou null si la cle est
    /// inconnue.
    /// </summary>
    public static Courriel? Rendre(string cle, string destinataire) => cle switch
    {
        "confirmation" => ModelesCourriel.Confirmation(
            destinataire, "Camille", LienExemple),

        "reinitialisation" => ModelesCourriel.Reinitialisation(
            destinataire, "Camille", LienExemple, 30),

        "mot-de-passe-change" => ModelesCourriel.MotDePasseChange(
            destinataire, "Camille", LienExemple),

        "nouvelle-connexion" => ModelesCourriel.NouvelleConnexion(
            destinataire, "Camille", "Chrome sur Windows", "203.0.113.42",
            // Une date fixe, et non « maintenant » : deux apercus du
            // meme modele doivent se ressembler, sans quoi on ne voit
            // plus ce qui a change entre deux relectures.
            new DateTime(2026, 4, 12, 9, 41, 0, DateTimeKind.Utc), LienExemple),

        "double-authentification" => ModelesCourriel.DoubleAuthentification(
            destinataire, "Camille", true, LienExemple),

        "compte-inactif" => ModelesCourriel.CompteInactif(
            destinataire, "Camille", new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)),

        "compte-efface" => ModelesCourriel.CompteEfface(
            destinataire, "Camille", 2),

        "candidature-recue" => ModelesCourriel.CandidatureRecue(
            destinataire, "Camille", "Developpeur Full Stack", "TechCorp", LienExemple),

        "nouvelle-candidature" => ModelesCourriel.NouvelleCandidature(
            destinataire, "Dominique", "Camille Martin", "Developpeur Full Stack",
            LienExemple, "Marseille", 82),

        "statut-candidature" => ModelesCourriel.StatutCandidature(
            destinataire, "Camille", "Developpeur Full Stack", "TechCorp",
            "Interview", LienExemple),

        "confirmation-newsletter" => ModelesCourriel.ConfirmationNewsletter(
            destinataire, "Camille", LienExemple, LienExemple),

        "accuse-signalement" => ModelesCourriel.AccuseSignalement(
            destinataire, "DSA-2026-000042", "Offre frauduleuse",
            new DateTime(2026, 4, 12, 9, 41, 0, DateTimeKind.Utc)),

        "decision-signalement" => ModelesCourriel.DecisionSignalement(
            destinataire, "DSA-2026-000042", true,
            "L'offre demandait un versement prealable au candidat, ce que la "
            + "loi interdit et que nos conditions d'utilisation prohibent.",
            "Contenu retire"),

        "essai" => ModelesCourriel.Essai(destinataire),

        _ => null,
    };
}
