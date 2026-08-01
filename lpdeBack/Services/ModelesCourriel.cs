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
                  Il part automatiquement, mais il n'est pas sans retour : repondez a ce message
                  si quelque chose vous parait anormal, quelqu'un vous lira.
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

    /// <summary>
    /// Previent qu'un compte inactif sera ferme, et comment l'eviter.
    ///
    /// Le geste demande est le plus simple possible : se connecter. Pas
    /// de lien a cliquer, pas de formulaire — une seule connexion repousse
    /// l'echeance de deux ans.
    /// </summary>
    public static Courriel CompteInactif(string destinataire, string prenom, DateTime echeance)
    {
        const string titre = "Votre compte sera ferme faute d'activite";
        var quand = echeance.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));

        var corps = $"""
            <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
            <p style="margin:0 0 12px">
              Nous n'avons pas vu votre compte depuis pres de deux ans. Nous ne conservons
              pas les comptes inactifs au-dela de ce delai : le votre sera efface le
              <strong>{E(quand)}</strong>, avec vos candidatures et votre CV.
            </p>
            <p style="margin:0 0 12px">
              Pour le garder, il suffit de vous connecter une fois. Rien d'autre a faire.
            </p>
            <p style="margin:0">
              Si vous preferez le laisser partir, vous n'avez rien a faire non plus.
            </p>
            """;
        var texte = $"""
            Bonjour {prenom},

            Nous n'avons pas vu votre compte depuis pres de deux ans. Il sera
            efface le {quand}, avec vos candidatures et votre CV.

            Pour le garder, connectez-vous une fois. Rien d'autre a faire.

            Si vous preferez le laisser partir, vous n'avez rien a faire non plus.

            {Marque}
            """;
        return new Courriel(destinataire, $"{titre} — {Marque}", Envelopper(titre, corps), texte);
    }

    /// <summary>
    /// Confirme l'effacement d'un compte, a l'adresse qui vient de le
    /// quitter.
    ///
    /// C'est le dernier message que cette adresse recevra, et le seul
    /// moyen pour son titulaire d'apprendre qu'on a efface son compte
    /// s'il n'en est pas l'auteur.
    /// </summary>
    public static Courriel CompteEfface(string destinataire, string prenom, int fichiers)
    {
        const string titre = "Votre compte a ete efface";
        var mention = fichiers > 0
            ? "Votre CV a ete supprime de nos serveurs."
            : "Aucun fichier n'etait conserve pour ce compte.";

        var corps = $"""
            <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
            <p style="margin:0 0 12px">
              Votre compte vient d'etre efface, ainsi que vos candidatures, vos recherches
              enregistrees, vos alertes et vos sections de CV. {E(mention)}
            </p>
            <p style="margin:0 0 12px">
              Les avis d'entreprise et les salaires que vous aviez partages restent en
              ligne, mais ils ne portent plus votre nom.
            </p>
            <p style="margin:0">
              Cette operation est definitive : nous ne pouvons pas la defaire. Si vous
              n'en etes pas l'auteur, ecrivez-nous sans tarder.
            </p>
            """;
        var texte = $"""
            Bonjour {prenom},

            Votre compte vient d'etre efface, ainsi que vos candidatures, vos
            recherches enregistrees, vos alertes et vos sections de CV.
            {mention}

            Les avis d'entreprise et les salaires que vous aviez partages restent
            en ligne, mais ils ne portent plus votre nom.

            Cette operation est definitive. Si vous n'en etes pas l'auteur,
            ecrivez-nous sans tarder.

            {Marque}
            """;
        return new Courriel(destinataire, $"{titre} — {Marque}", Envelopper(titre, corps), texte);
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
    // ══════════════════════════════════════
    //  Lettre d'information
    // ══════════════════════════════════════

    /// <summary>
    /// La confirmation d'abonnement.
    ///
    /// Elle part du canal transactionnel, pas de Brevo : quelqu'un qui
    /// vient de saisir son adresse attend ce message dans la minute, et
    /// c'est exactement ce que le transactionnel sait faire. Passer par la
    /// file d'une campagne le ferait arriver quand la file serait vide.
    /// </summary>
    public static Courriel ConfirmationNewsletter(string destinataire, string? prenom,
                                                  string lien, string lienDesinscription)
    {
        const string titre = "Confirmez votre abonnement";
        var bonjour = string.IsNullOrWhiteSpace(prenom) ? "Bonjour," : $"Bonjour {E(prenom)},";
        var corps = $"""
            <p style="margin:0 0 12px">{bonjour}</p>
            <p style="margin:0 0 12px">
              Vous venez de demander a recevoir la lettre d'information de {Marque} :
              les offres qui bougent, les metiers qui recrutent, et ce qui change sur la
              plateforme.
            </p>
            <p style="margin:0 0 12px">
              <strong>Un clic reste necessaire.</strong> Tant que vous ne l'avez pas fait,
              nous ne vous enverrons rien — c'est ce qui garantit que personne ne peut
              vous abonner a votre place.
            </p>
            <p style="margin:0;font-size:13px;color:#577177">
              Ce n'etait pas vous ? Ignorez ce message, il ne se passera rien. Ou
              <a href="{lienDesinscription}" style="color:#577177">retirez definitivement cette adresse</a>
              pour qu'on cesse de vous la proposer.
            </p>
            """;
        var texte = $"""
            {bonjour.Replace("&#39;", "'")}

            Vous venez de demander a recevoir la lettre d'information de {Marque}.
            Confirmez en ouvrant ce lien :

            {lien}

            Tant que vous ne l'avez pas fait, nous ne vous enverrons rien.

            Ce n'etait pas vous ? Ignorez ce message, il ne se passera rien. Ou
            retirez definitivement cette adresse :

            {lienDesinscription}

            {Marque}
            """;
        return new Courriel(destinataire, $"{titre} — {Marque}",
            Envelopper(titre, corps, "Confirmer mon abonnement", lien), texte);
    }

    /// <summary>
    /// L'enveloppe d'une campagne.
    ///
    /// Volontairement plus sobre que celle des messages de securite : une
    /// lettre d'information se lit d'un trait, et le corps y est ecrit par
    /// une personne, pas par le code. L'enveloppe se contente de porter la
    /// marque, la ligne d'apercu et le pied de desinscription.
    /// </summary>
    public static string EnveloppeNewsletter(string sujet, string apercu, string corps, string pied)
    {
        // Le texte d'apercu, cache mais lu par les messageries dans la liste
        // des messages. Sans lui elles affichent les premiers mots du corps.
        var preheader = string.IsNullOrWhiteSpace(apercu) ? "" : $"""
            <div style="display:none;max-height:0;overflow:hidden;opacity:0">{apercu}</div>
            """;

        return $"""
            <!doctype html>
            <html lang="fr"><head><meta charset="utf-8" />
            <meta name="viewport" content="width=device-width,initial-scale=1" />
            <title>{E(sujet)}</title></head>
            <body style="margin:0;padding:24px 12px;background:#ffecd1;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif">
              {preheader}
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%"
                     style="max-width:600px;margin:0 auto;background:#ffffff;border:1px solid #ebdac1;border-radius:14px;overflow:hidden">
                <tr><td style="padding:20px 32px;background:#001524;color:#ffecd1;font-size:15px;font-weight:600;
                       letter-spacing:.02em">{Marque}</td></tr>
                <tr><td style="padding:28px 32px;font-size:15px;line-height:1.7;color:#10272b">{corps}</td></tr>
                <tr><td style="padding:18px 32px;border-top:1px solid #ebdac1;font-size:12px;line-height:1.6;color:#81999e">
                  {pied}
                </td></tr>
              </table>
            </body></html>
            """;
    }

    // ══════════════════════════════════════
    //  Candidatures
    //
    //  Tout le cycle se deroulait jusqu'ici sans un seul courriel :
    //  notification interne, temps reel, notification poussee — trois
    //  canaux qui supposent d'etre sur le site. Un recruteur qui ne
    //  l'ouvre pas ne savait pas qu'on lui avait ecrit, et un candidat
    //  n'avait aucune trace de sa demarche dans sa boite.
    // ══════════════════════════════════════

    /// <summary>Au candidat, quand sa candidature est enregistree.</summary>
    public static Courriel CandidatureRecue(string destinataire, string prenom,
                                            string poste, string entreprise, string lien)
    {
        const string titre = "Votre candidature est bien partie";
        var corps = $"""
            <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
            <p style="margin:0 0 12px">
              Votre candidature au poste de <strong>{E(poste)}</strong> chez
              <strong>{E(entreprise)}</strong> vient d'etre transmise au recruteur.
            </p>
            <p style="margin:0 0 12px">
              Vous serez prevenu ici des qu'elle change d'etat. D'ici la, elle reste
              consultable — et modifiable tant que personne ne l'a ouverte — depuis
              le suivi de vos candidatures.
            </p>
            <p style="margin:0">
              Gardez ce message : c'est la trace de votre demarche, avec sa date.
            </p>
            """;
        var texte = $"""
            Bonjour {prenom},

            Votre candidature au poste de {poste} chez {entreprise} vient d'etre
            transmise au recruteur.

            Suivez-la ici : {lien}

            {Marque}
            """;
        return new Courriel(destinataire, $"Candidature envoyee — {poste}",
                            Envelopper(titre, corps, "Suivre ma candidature", lien), texte);
    }

    /// <summary>Au recruteur, quand quelqu'un postule a son offre.</summary>
    public static Courriel NouvelleCandidature(string destinataire, string prenomRecruteur,
                                               string candidat, string poste, string lien,
                                               string? ville, int? score)
    {
        const string titre = "Une nouvelle candidature vous attend";

        // Le detail utile tient en deux lignes : qui, pour quel poste, et
        // d'ou. Le score n'apparait que s'il veut dire quelque chose —
        // c'est-a-dire si le recruteur a pose des questions.
        var precisions = new List<string>();
        if (!string.IsNullOrWhiteSpace(ville)) precisions.Add($"Depuis {E(ville)}");
        if (score is { } sc) precisions.Add($"Reponses aux questions : {sc} %");
        var ligne = precisions.Count > 0
            ? $"""<p style="margin:0 0 12px;color:#577177">{string.Join(" &middot; ", precisions)}</p>"""
            : "";

        var corps = $"""
            <p style="margin:0 0 12px">Bonjour {E(prenomRecruteur)},</p>
            <p style="margin:0 0 12px">
              <strong>{E(candidat)}</strong> vient de postuler a votre offre
              <strong>{E(poste)}</strong>.
            </p>
            {ligne}
            <p style="margin:0">
              Une reponse rapide, meme negative, vaut mieux qu'un silence : c'est ce
              que les candidats retiennent d'une entreprise.
            </p>
            """;
        var texte = $"""
            Bonjour {prenomRecruteur},

            {candidat} vient de postuler a votre offre {poste}.
            {(string.IsNullOrWhiteSpace(ville) ? "" : $"Depuis {ville}.")}

            Ouvrir la candidature : {lien}

            {Marque}
            """;
        return new Courriel(destinataire, $"Nouvelle candidature — {poste}",
                            Envelopper(titre, corps, "Ouvrir la candidature", lien), texte);
    }

    /// <summary>
    /// Au candidat, quand le recruteur decide.
    ///
    /// Le ton suit la nouvelle : un refus ne se felicite pas, et une
    /// acceptation ne se murmure pas. Le message dit toujours ce qui se
    /// passe ensuite, meme quand la reponse est non — c'est ce qui
    /// distingue une reponse d'un silence administratif.
    /// </summary>
    public static Courriel StatutCandidature(string destinataire, string prenom,
                                             string poste, string entreprise,
                                             string statut, string lien)
    {
        var (titre, phrase, suite) = statut switch
        {
            "Accepted" => ("Votre candidature est retenue",
                $"Bonne nouvelle : <strong>{E(entreprise)}</strong> retient votre candidature au poste de <strong>{E(poste)}</strong>.",
                "Le recruteur va vous contacter. Vous pouvez aussi lui ecrire directement depuis la messagerie."),
            "Rejected" => ("Votre candidature n'a pas ete retenue",
                $"<strong>{E(entreprise)}</strong> ne donnera pas suite a votre candidature au poste de <strong>{E(poste)}</strong>.",
                "Ce n'est pas un jugement sur votre parcours : une offre se joue souvent sur un detail de calendrier ou de perimetre. D'autres offres proches de celle-ci vous attendent."),
            "Reviewed" => ("Votre candidature a ete consultee",
                $"<strong>{E(entreprise)}</strong> a ouvert votre candidature au poste de <strong>{E(poste)}</strong>.",
                "Rien n'est decide a ce stade. Vous serez prevenu des qu'une suite sera donnee."),
            _ => ("Votre candidature a change d'etat",
                $"Votre candidature au poste de <strong>{E(poste)}</strong> chez <strong>{E(entreprise)}</strong> a change d'etat.",
                "Le detail est consultable dans le suivi de vos candidatures."),
        };

        var corps = $"""
            <p style="margin:0 0 12px">Bonjour {E(prenom)},</p>
            <p style="margin:0 0 12px">{phrase}</p>
            <p style="margin:0">{suite}</p>
            """;
        var texte = $"""
            Bonjour {prenom},

            {WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(phrase, "<.*?>", ""))}

            Voir le detail : {lien}

            {Marque}
            """;
        return new Courriel(destinataire, $"{titre} — {poste}",
                            Envelopper(titre, corps, "Voir ma candidature", lien), texte);
    }
}
