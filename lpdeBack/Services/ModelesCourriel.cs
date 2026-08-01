using System.Net;

namespace lpdeBack.Services;

/// <summary>
/// Les messages que la plateforme adresse a ses membres.
///
/// Ils sont ecrits ici, en francais et en toutes lettres, plutot que
/// disperses dans les controleurs : un courriel de securite se relit, et
/// celui qui le relit ne doit pas avoir a lire du C# pour cela.
///
/// Regle de fond : un message de securite dit ce qui vient de se passer,
/// ce que le destinataire doit faire s'il n'en est pas l'auteur, et
/// combien de temps le lien reste valable. Aucun de ces trois points
/// n'est decoratif.
/// </summary>
public static class ModelesCourriel
{
    private const string Marque = "La Plateforme de l'emploi";

    /// <summary>Le gabarit commun. Les couleurs suivent celles du site.</summary>
    private static string Envelopper(string titre, string corps, string? bouton = null, string? lien = null)
    {
        var action = bouton != null && lien != null
            ? $"""
               <tr><td style="padding:8px 32px 24px">
                 <a href="{lien}" style="display:inline-block;padding:12px 22px;background:#15616d;color:#ffffff;
                    text-decoration:none;border-radius:8px;font-weight:600;font-size:15px">{bouton}</a>
               </td></tr>
               <tr><td style="padding:0 32px 28px;font-size:12px;line-height:1.6;color:#577177">
                 Si le bouton ne fonctionne pas, copiez cette adresse dans votre navigateur :<br />
                 <span style="word-break:break-all;color:#15616d">{lien}</span>
               </td></tr>
               """
            : "";

        return $"""
            <!doctype html>
            <html lang="fr"><head><meta charset="utf-8" />
            <meta name="viewport" content="width=device-width,initial-scale=1" />
            <title>{titre}</title></head>
            <body style="margin:0;padding:24px 12px;background:#ffecd1;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif">
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%"
                     style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #ebdac1;border-radius:14px;overflow:hidden">
                <tr><td style="padding:22px 32px;background:#001524;color:#ffecd1;font-size:15px;font-weight:600;
                       letter-spacing:.02em">{Marque}</td></tr>
                <tr><td style="padding:28px 32px 8px">
                  <h1 style="margin:0 0 12px;font-size:19px;line-height:1.35;color:#10272b">{titre}</h1>
                </td></tr>
                <tr><td style="padding:0 32px 20px;font-size:14px;line-height:1.65;color:#39545a">{corps}</td></tr>
                {action}
                <tr><td style="padding:18px 32px;border-top:1px solid #ebdac1;font-size:12px;line-height:1.6;color:#81999e">
                  Ce message vous est adresse parce qu'un compte existe a cette adresse sur {Marque}.
                  Il est envoye automatiquement : merci de ne pas y repondre.
                </td></tr>
              </table>
            </body></html>
            """;
    }

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    // ══════════════════════════════════════
    //  Mot de passe oublie
    // ══════════════════════════════════════

    public static Courriel Reinitialisation(string destinataire, string prenom, string lien, int minutes)
    {
        const string titre = "Reinitialiser votre mot de passe";
        var corps = $"""
            <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
            <p style="margin:0 0 12px">
              Quelqu'un — vous, nous l'esperons — a demande a reinitialiser le mot de passe
              de votre compte. Le lien ci-dessous vous conduit vers un formulaire ou en
              choisir un nouveau.
            </p>
            <p style="margin:0 0 12px">
              <strong>Ce lien expire dans {minutes} minutes</strong> et ne fonctionne qu'une fois.
            </p>
            <p style="margin:0">
              Si vous n'avez rien demande, ignorez ce message : votre mot de passe actuel
              reste valable et personne n'a eu acces a votre compte.
            </p>
            """;
        var texte = $"""
            Bonjour {prenom},

            Quelqu'un a demande a reinitialiser le mot de passe de votre compte.
            Ouvrez ce lien pour en choisir un nouveau :

            {lien}

            Ce lien expire dans {minutes} minutes et ne fonctionne qu'une fois.

            Si vous n'avez rien demande, ignorez ce message : votre mot de passe
            actuel reste valable.

            {Marque}
            """;
        return new Courriel(destinataire, $"{titre} — {Marque}", Envelopper(titre, corps, "Choisir un nouveau mot de passe", lien), texte);
    }

    // ══════════════════════════════════════
    //  Confirmation d'adresse
    // ══════════════════════════════════════

    public static Courriel Confirmation(string destinataire, string prenom, string lien)
    {
        const string titre = "Confirmez votre adresse e-mail";
        var corps = $"""
            <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
            <p style="margin:0 0 12px">
              Bienvenue sur {Marque}. Il reste une chose a faire : confirmer que cette
              adresse est bien la votre.
            </p>
            <p style="margin:0">
              Sans cette confirmation, nous ne pourrons pas vous prevenir des reponses a
              vos candidatures, ni vous permettre de recuperer votre mot de passe si vous
              l'oubliez.
            </p>
            """;
        var texte = $"""
            Bonjour {prenom},

            Bienvenue sur {Marque}. Confirmez votre adresse en ouvrant ce lien :

            {lien}

            Sans cette confirmation, nous ne pourrons pas vous prevenir des reponses
            a vos candidatures, ni vous permettre de recuperer votre mot de passe.

            {Marque}
            """;
        return new Courriel(destinataire, $"{titre} — {Marque}", Envelopper(titre, corps, "Confirmer mon adresse", lien), texte);
    }

    // ══════════════════════════════════════
    //  Evenements de securite
    // ══════════════════════════════════════

    /// <summary>
    /// Une connexion depuis un appareil inconnu. C'est le seul message qui
    /// permette a quelqu'un de decouvrir que son compte est compromis :
    /// il nomme donc l'appareil, l'endroit et l'heure, et dit quoi faire.
    /// </summary>
    public static Courriel NouvelleConnexion(string destinataire, string prenom, string appareil, string ip, DateTime quand, string lienSecurite)
    {
        const string titre = "Nouvelle connexion a votre compte";
        var corps = $"""
            <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
            <p style="margin:0 0 12px">Votre compte vient d'etre utilise depuis un appareil que nous ne connaissions pas.</p>
            <table role="presentation" cellpadding="0" cellspacing="0" border="0"
                   style="margin:0 0 14px;font-size:13px;color:#39545a">
              <tr><td style="padding:2px 16px 2px 0;color:#81999e">Quand</td><td>{quand:dd/MM/yyyy 'a' HH'h'mm} (UTC)</td></tr>
              <tr><td style="padding:2px 16px 2px 0;color:#81999e">Appareil</td><td>{E(appareil)}</td></tr>
              <tr><td style="padding:2px 16px 2px 0;color:#81999e">Adresse IP</td><td>{E(ip)}</td></tr>
            </table>
            <p style="margin:0 0 12px">Si c'etait vous, il n'y a rien a faire.</p>
            <p style="margin:0">
              <strong>Sinon, changez votre mot de passe sans attendre</strong> et deconnectez
              les appareils que vous ne reconnaissez pas. La page Securite de votre compte
              permet les deux.
            </p>
            """;
        var texte = $"""
            Bonjour {prenom},

            Votre compte vient d'etre utilise depuis un appareil inconnu.

              Quand    : {quand:dd/MM/yyyy} a {quand:HH}h{quand:mm} (UTC)
              Appareil : {appareil}
              IP       : {ip}

            Si c'etait vous, il n'y a rien a faire. Sinon, changez votre mot de passe
            sans attendre et deconnectez les appareils inconnus :

            {lienSecurite}

            {Marque}
            """;
        return new Courriel(destinataire, $"{titre} — {Marque}",
            Envelopper(titre, corps, "Ouvrir la page Securite", lienSecurite), texte);
    }

    /// <summary>La 2FA vient d'etre activee ou coupee : dans les deux cas cela se signale.</summary>
    public static Courriel DoubleAuthentification(string destinataire, string prenom, bool activee, string lienSecurite)
    {
        var titre = activee
            ? "La double authentification est activee"
            : "La double authentification a ete desactivee";

        var corps = activee
            ? $"""
              <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
              <p style="margin:0 0 12px">
                Votre compte demande desormais un code a six chiffres en plus de votre mot
                de passe. Meme si quelqu'un devinait ce mot de passe, il ne pourrait pas
                entrer.
              </p>
              <p style="margin:0">
                <strong>Conservez vos codes de secours</strong> ailleurs que sur le telephone
                qui genere les codes : ce sont eux, et eux seuls, qui vous rouvriront la porte
                si vous perdez l'appareil.
              </p>
              """
            : $"""
              <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
              <p style="margin:0 0 12px">
                La double authentification vient d'etre desactivee sur votre compte. Votre
                mot de passe suffit de nouveau pour s'y connecter.
              </p>
              <p style="margin:0">
                <strong>Si vous n'en etes pas l'auteur, votre compte est compromis</strong> :
                changez votre mot de passe immediatement et reactivez la double
                authentification.
              </p>
              """;

        var texte = activee
            ? $"""
              Bonjour {prenom},

              La double authentification est active sur votre compte. Un code a six
              chiffres sera desormais demande en plus de votre mot de passe.

              Conservez vos codes de secours ailleurs que sur le telephone qui genere
              les codes : ce sont eux qui vous rouvriront la porte si vous le perdez.

              {lienSecurite}

              {Marque}
              """
            : $"""
              Bonjour {prenom},

              La double authentification vient d'etre desactivee sur votre compte.
              Votre mot de passe suffit de nouveau pour s'y connecter.

              Si vous n'en etes pas l'auteur, votre compte est compromis : changez
              votre mot de passe immediatement.

              {lienSecurite}

              {Marque}
              """;

        return new Courriel(destinataire, $"{titre} — {Marque}",
            Envelopper(titre, corps, "Ouvrir la page Securite", lienSecurite), texte);
    }

    /// <summary>Le mot de passe a change. Envoye apres coup, il sert d'alarme.</summary>
    public static Courriel MotDePasseChange(string destinataire, string prenom, string lienSecurite)
    {
        const string titre = "Votre mot de passe a ete modifie";
        var corps = $"""
            <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
            <p style="margin:0 0 12px">
              Le mot de passe de votre compte vient d'etre modifie. Par precaution, toutes
              vos autres sessions ont ete fermees : il faudra vous reconnecter sur vos
              autres appareils.
            </p>
            <p style="margin:0">
              Si vous n'en etes pas l'auteur, demandez immediatement une reinitialisation
              depuis la page de connexion, puis contactez-nous.
            </p>
            """;
        var texte = $"""
            Bonjour {prenom},

            Le mot de passe de votre compte vient d'etre modifie. Toutes vos autres
            sessions ont ete fermees par precaution.

            Si vous n'en etes pas l'auteur, demandez immediatement une
            reinitialisation depuis la page de connexion.

            {lienSecurite}

            {Marque}
            """;
        return new Courriel(destinataire, $"{titre} — {Marque}",
            Envelopper(titre, corps, "Ouvrir la page Securite", lienSecurite), texte);
    }

    /// <summary>Message de controle, declenche depuis l'administration.</summary>
    public static Courriel Essai(string destinataire)
    {
        const string titre = "Essai d'expedition";
        var corps = """
            <p style="margin:0 0 12px">Ce message confirme que la plateforme sait envoyer du courriel.</p>
            <p style="margin:0">
              S'il vous parvient, les mots de passe oublies, les confirmations d'adresse et
              les alertes de connexion partiront eux aussi.
            </p>
            """;
        var texte = $"""
            Ce message confirme que la plateforme sait envoyer du courriel.

            S'il vous parvient, les mots de passe oublies, les confirmations d'adresse
            et les alertes de connexion partiront eux aussi.

            {Marque}
            """;
        return new Courriel(destinataire, $"{titre} — {Marque}", Envelopper(titre, corps), texte);
    }
}
