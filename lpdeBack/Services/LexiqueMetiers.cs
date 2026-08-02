using System.Text.RegularExpressions;

namespace lpdeBack.Services;

/// <summary>
/// Le vocabulaire du domaine.
///
/// Tout le reste du site compare des chaines de caracteres a des chaines
/// de caracteres. « Developpeur front-end » et « Dev front React H/F »
/// n'ont alors rien en commun, « js » et « JavaScript » non plus, et un
/// candidat qui a saisi « IDE » ne correspond a aucune annonce
/// d'infirmier. Les recommandations en portaient la trace : elles
/// croisaient les competences du profil avec les etiquettes de l'offre,
/// terme a terme, et ne trouvaient presque jamais rien.
///
/// Ce fichier est la reponse la moins couteuse a ce probleme. Un modele
/// de langage saurait rapprocher ces mots, mais il faudrait l'appeler
/// pour chaque paire candidat/offre — dix mille fois par page de
/// resultats. Un lexique le fait en microsecondes, se lit, s'explique,
/// et fonctionne quand le modele est eteint.
///
/// Il n'a pas vocation a etre exhaustif : il couvre les familles de
/// metiers que le catalogue contient reellement et les abreviations qui
/// coutent le plus cher quand on les ignore.
/// </summary>
public static class LexiqueMetiers
{
    // ══════════════════════════════════════
    //  Familles de metiers
    // ══════════════════════════════════════
    //
    // On ne cherche pas a nommer un metier precis — « developpeur back-end
    // Java senior » — mais le terrain sur lequel il se joue. C'est la
    // question a laquelle un score de correspondance doit repondre en
    // premier : est-on seulement dans le bon domaine ? Un candidat
    // infirmier a qui l'on propose un poste de developpeur parce que les
    // deux annonces contiennent le mot « equipe » a perdu confiance pour
    // de bon.

    /// <summary>Une famille, le nom qu'on affiche et tout ce qui la designe.</summary>
    public sealed record Famille(string Nom, string[] Termes);

    private static readonly Famille[] _familles =
    {
        new("Développement", new[]
        {
            "developpeur", "developpeuse", "dev", "developpement", "programmeur", "codeur",
            "ingenieur logiciel", "ingenieur etudes", "concepteur developpeur", "fullstack",
            "full stack", "front end", "back end", "frontend", "backend", "webmaster",
            "integrateur web", "lead technique", "architecte logiciel", "devops", "sre",
        }),
        new("Données", new[]
        {
            "data", "donnees", "data scientist", "data analyst", "data engineer",
            "analyste donnees", "statisticien", "business intelligence", "decisionnel",
            "machine learning", "intelligence artificielle",
        }),
        new("Systèmes et réseaux", new[]
        {
            "administrateur systeme", "administrateur reseau", "sysadmin", "reseaux",
            "infrastructure", "technicien informatique", "technicien support",
            "support informatique", "helpdesk", "hotline informatique", "cybersecurite",
            "securite informatique", "pentester",
        }),
        new("Gestion de projet", new[]
        {
            "chef de projet", "product owner", "product manager", "scrum master",
            "consultant fonctionnel", "assistant a maitrise ouvrage", "amoa", "moe",
            "coordinateur projet", "directeur de projet",
        }),
        new("Design", new[]
        {
            "designer", "graphiste", "ux", "ui", "directeur artistique",
            "infographiste", "motion designer", "illustrateur", "maquettiste",
        }),
        new("Marketing et communication", new[]
        {
            "marketing", "communication", "community manager", "charge de communication",
            "traffic manager", "seo", "sea", "growth", "referencement", "attache de presse",
            "content manager", "redacteur web",
        }),
        new("Commerce et vente", new[]
        {
            "commercial", "commerciale", "vendeur", "vendeuse", "vente",
            "business developer", "account manager", "charge d'affaires", "technico commercial",
            "conseiller clientele", "conseillere clientele", "responsable de magasin",
            "chef de rayon", "caissier", "caissiere", "hote de caisse", "telepros",
            "teleconseiller", "televendeur",
        }),
        new("Comptabilité et finance", new[]
        {
            "comptable", "comptabilite", "controleur de gestion", "auditeur", "audit",
            "expert comptable", "gestionnaire de paie", "paie", "tresorerie",
            "analyste financier", "credit manager",
        }),
        new("Banque et assurance", new[]
        {
            "banque", "assurance", "conseiller bancaire", "charge de clientele",
            "gestionnaire de sinistres", "courtier", "actuaire", "souscripteur",
        }),
        new("Ressources humaines", new[]
        {
            "ressources humaines", "rh", "recruteur", "recrutement", "charge de recrutement",
            "talent acquisition", "responsable formation", "gestionnaire rh", "drh",
        }),
        new("Juridique", new[]
        {
            "juriste", "juridique", "avocat", "notaire", "clerc", "conformite", "compliance",
            "assistant juridique", "paralegal",
        }),
        new("Administratif", new[]
        {
            "secretaire", "assistant administratif", "assistante administrative",
            "agent administratif", "gestionnaire administratif", "office manager",
            "assistant de direction", "standardiste", "accueil",
        }),
        new("Logistique et transport", new[]
        {
            "logistique", "cariste", "magasinier", "preparateur de commandes",
            "chauffeur", "conducteur", "livreur", "transport", "manutentionnaire",
            "agent de quai", "affreteur", "responsable entrepot", "supply chain",
        }),
        new("Bâtiment et travaux publics", new[]
        {
            "macon", "plombier", "electricien", "chauffagiste", "menuisier", "peintre",
            "carreleur", "couvreur", "charpentier", "platrier", "conducteur de travaux",
            "chef de chantier", "btp", "gros oeuvre", "second oeuvre", "terrassier",
            "grutier", "coffreur", "serrurier", "vitrier",
        }),
        new("Industrie et maintenance", new[]
        {
            "technicien de maintenance", "maintenance", "operateur de production",
            "agent de production", "regleur", "usineur", "tourneur", "fraiseur",
            "soudeur", "chaudronnier", "ajusteur", "monteur", "mecanicien",
            "electromecanicien", "automaticien", "methodes", "qualite industrielle",
            "responsable production", "chef d'equipe production",
        }),
        new("Restauration et hôtellerie", new[]
        {
            "cuisinier", "cuisiniere", "chef de cuisine", "commis de cuisine", "patissier",
            "boulanger", "boucher", "charcutier", "serveur", "serveuse", "barman",
            "plongeur", "restauration", "hotellerie", "receptionniste", "gouvernante",
            "employe polyvalent restauration", "maitre d'hotel",
        }),
        new("Santé", new[]
        {
            "infirmier", "infirmiere", "ide", "aide soignant", "aide soignante",
            "medecin", "pharmacien", "preparateur en pharmacie", "kinesitherapeute",
            "sage femme", "dentiste", "orthophoniste", "ergotherapeute", "psychologue",
            "manipulateur radio", "ambulancier", "auxiliaire de puericulture",
            "agent de service hospitalier", "aide medico psychologique", "amp",
        }),
        new("Social et service à la personne", new[]
        {
            "educateur specialise", "assistant social", "auxiliaire de vie",
            "aide a domicile", "moniteur educateur", "animateur", "conseiller insertion",
            "accompagnant educatif", "aes", "mediateur social", "garde d'enfants",
        }),
        new("Enseignement", new[]
        {
            "enseignant", "professeur", "formateur", "instituteur", "surveillant",
            "atsem", "educateur sportif", "coach sportif",
        }),
        new("Sécurité", new[]
        {
            "agent de securite", "agent de surveillance", "maitre chien", "ssiap",
            "gardien", "vigile", "pompier", "policier", "gendarme",
        }),
        new("Propreté et environnement", new[]
        {
            "agent d'entretien", "agent de proprete", "nettoyage", "technicien de surface",
            "ripeur", "agent de dechetterie", "espaces verts", "jardinier", "paysagiste",
        }),
        new("Agriculture", new[]
        {
            "agricole", "ouvrier agricole", "viticole", "vendangeur", "arboriculteur",
            "eleveur", "maraicher", "tractoriste",
        }),
        new("Immobilier", new[]
        {
            "immobilier", "agent immobilier", "negociateur immobilier", "gestionnaire locatif",
            "syndic", "gardien d'immeuble", "expert immobilier",
        }),
        new("Tourisme et loisirs", new[]
        {
            "tourisme", "agent de voyage", "guide touristique", "hote d'accueil",
            "animateur touristique", "steward", "hotesse de l'air",
        }),
    };

    // ══════════════════════════════════════
    //  Synonymes de competences
    // ══════════════════════════════════════
    //
    // Chaque groupe se replie sur son premier terme. On ne traite que les
    // ecarts qui coutent quelque chose : ceux ou deux ecritures du meme
    // savoir-faire sont assez courantes pour qu'un candidat en choisisse
    // une et un employeur l'autre. Enumerer tous les outils du monde
    // serait sans fin et sans gain — les termes que le lexique ignore
    // sont simplement compares tels quels, ce qui reste correct.

    private static readonly string[][] _synonymes =
    {
        new[] { "javascript", "js", "ecmascript", "es6" },
        new[] { "typescript", "ts" },
        new[] { "react", "reactjs", "react native" },
        new[] { "angular", "angularjs", "angular2" },
        new[] { "vue", "vuejs", "vue js" },
        new[] { "nodejs", "node", "node js" },
        // « net » n'y figure pas, et c'est voulu : « salaire net » et
        // « resultat net » sont partout dans les annonces, et le repli en
        // aurait fait autant de developpeurs .NET. La forme « .NET »
        // est deja reconnue par « Aplatir », qui l'epelle « dotnet ».
        new[] { "csharp", "c sharp", "dotnet", "asp net", "aspnet" },
        new[] { "cplusplus", "c++" },
        new[] { "python", "py", "django", "flask" },
        new[] { "php", "symfony", "laravel" },
        new[] { "java", "spring", "spring boot", "jee", "j2ee" },
        new[] { "sql", "mysql", "postgresql", "postgres", "sqlserver", "oracle", "mariadb" },
        new[] { "nosql", "mongodb", "redis", "elasticsearch" },
        new[] { "docker", "kubernetes", "k8s", "conteneur" },
        // Le premier terme de chaque groupe est celui qu'on affichera :
        // « cloud en commun » se lit, « aws en commun » induit en erreur
        // quand le candidat connait Azure.
        new[] { "cloud", "aws", "amazon web services", "azure", "gcp" },
        new[] { "git", "github", "gitlab", "versioning" },
        new[] { "html", "html5", "css", "css3", "sass", "scss", "tailwind", "bootstrap" },
        new[] { "suite adobe", "photoshop", "illustrator", "indesign", "adobe" },
        new[] { "figma", "sketch", "adobe xd", "maquettage" },
        new[] { "bureautique", "excel", "tableur", "pack office", "microsoft office", "word" },
        new[] { "sap", "erp", "progiciel" },
        new[] { "salesforce", "crm", "hubspot" },
        new[] { "comptabilite", "compta", "sage", "cegid" },
        new[] { "anglais", "english", "bilingue anglais" },
        new[] { "espagnol", "spanish" },
        new[] { "allemand", "german", "deutsch" },
        new[] { "permis b", "permis de conduire", "permis voiture" },
        new[] { "caces", "chariot elevateur", "engin de manutention" },
        new[] { "habilitation electrique", "h0b0", "b1v" },
        new[] { "ssiap", "securite incendie" },
        new[] { "haccp", "hygiene alimentaire" },
        new[] { "relation client", "service client", "accueil client", "sens du contact" },
        new[] { "management", "encadrement", "gestion d'equipe", "manager" },
        new[] { "gestion de projet", "conduite de projet", "chefferie de projet" },
        new[] { "agile", "scrum", "kanban", "methode agile" },
    };

    // ══════════════════════════════════════
    //  Types de contrat
    // ══════════════════════════════════════
    //
    // Le champ « ContractType » d'une offre est une chaine libre, remplie
    // differemment par chaque source d'import. On la ramene a un mot.

    private static readonly (string Contrat, string[] Termes)[] _contrats =
    {
        // L'alternance passe avant le CDD et le CDI : un contrat
        // d'apprentissage est juridiquement un CDD, et de nombreuses
        // annonces portent les deux mentions. Le candidat qui cherche une
        // alternance ne cherche pas un CDD.
        ("Alternance", new[] { "alternance", "alternant", "apprentissage", "apprenti", "contrat pro", "professionnalisation" }),
        ("Stage", new[] { "stage", "stagiaire", "internship", "stage conventionne" }),
        ("Interim", new[] { "interim", "interimaire", "mission temporaire", "travail temporaire" }),
        ("Freelance", new[] { "freelance", "independant", "portage", "prestation", "auto entrepreneur" }),
        ("CDD", new[] { "cdd", "duree determinee", "contrat a duree determinee", "saisonnier" }),
        ("CDI", new[] { "cdi", "duree indeterminee", "contrat a duree indeterminee", "permanent" }),
    };

    /// <summary>
    /// Mots trop frequents pour distinguer quoi que ce soit.
    ///
    /// Sans cette liste, deux annonces sans rapport se ressemblent des
    /// qu'elles disent toutes deux « nous recherchons un profil motive
    /// pour rejoindre notre equipe » — c'est-a-dire toujours.
    /// </summary>
    private static readonly HashSet<string> _motsVides = new(StringComparer.Ordinal)
    {
        "le", "la", "les", "un", "une", "des", "du", "de", "au", "aux", "et", "ou", "en",
        "dans", "pour", "par", "sur", "sous", "avec", "sans", "chez", "vers", "a", "d", "l",
        "ce", "cette", "ces", "son", "sa", "ses", "leur", "leurs", "notre", "nos", "votre", "vos",
        "qui", "que", "quoi", "dont", "est", "sont", "etre", "avoir", "vous", "nous", "il", "elle",
        "recherche", "recherchons", "recrute", "recrutons", "poste", "offre", "emploi", "job",
        "mission", "missions", "profil", "profils", "candidat", "candidate", "equipe", "societe",
        "entreprise", "groupe", "societes", "notamment", "ainsi", "plus", "tres", "bien", "tout",
        "toute", "tous", "toutes", "h", "f", "hf", "fh", "nouveau", "nouvelle", "urgent",
    };

    // ── Index construits une fois ──
    //
    // Le lexique est parcouru pour chaque paire candidat/offre, soit
    // plusieurs milliers de fois par recherche. Une recherche lineaire
    // dans les tableaux ci-dessus y serait sensible ; un dictionnaire
    // construit au chargement ne l'est pas.

    private static readonly Dictionary<string, string> _canon = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> _motUnique = new(StringComparer.Ordinal);
    private static readonly List<(string Expression, string Nom)> _expressions = new();

    static LexiqueMetiers()
    {
        foreach (var groupe in _synonymes)
        {
            var canonique = Aplatir(groupe[0]);
            foreach (var variante in groupe)
                _canon[Aplatir(variante)] = canonique;
        }

        foreach (var famille in _familles)
        {
            foreach (var terme in famille.Termes)
            {
                var plat = Aplatir(terme);
                if (plat.Contains(' '))
                    _expressions.Add((plat, famille.Nom));
                else
                    _motUnique.TryAdd(plat, famille.Nom);
            }
        }

        // Les expressions longues passent avant les courtes : « chef de
        // projet » doit gagner contre « projet », et « conducteur de
        // travaux » contre « conducteur », sans quoi un conducteur de
        // travaux serait classe parmi les chauffeurs.
        _expressions.Sort((a, b) => b.Expression.Length.CompareTo(a.Expression.Length));
    }

    // ══════════════════════════════════════
    //  Normalisation
    // ══════════════════════════════════════

    /// <summary>
    /// Minuscules, sans accents, sans ponctuation — a une exception pres.
    ///
    /// Retirer la ponctuation sans precaution detruit une partie du
    /// vocabulaire technique : « C++ » devient « c », « C# » devient « c »
    /// lui aussi, et « .NET » devient « net », qui est par ailleurs un mot
    /// francais. Trois competences distinctes se confondraient alors en
    /// une, et la plus courante — le langage C — absorberait les deux
    /// autres. On epelle donc ces cas avant d'aplatir le reste.
    /// </summary>
    public static string Aplatir(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return string.Empty;

        var t = GeoUtils.Normalize(texte);

        t = t.Replace("c++", " cplusplus ")
             .Replace("c#", " csharp ")
             .Replace(".net", " dotnet ")
             .Replace("node.js", " nodejs ")
             .Replace("vue.js", " vuejs ")
             .Replace("react.js", " reactjs ");

        t = Regex.Replace(t, @"[^a-z0-9]+", " ");
        return string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Les termes significatifs d'un texte, ramenes a leur forme canonique.
    ///
    /// Accepte aussi bien une liste separee par des virgules — le champ
    /// « Skills » d'un profil, les « Tags » d'une offre — qu'une phrase
    /// entiere. Les expressions du lexique sont reconnues avant le
    /// decoupage en mots : « gestion de projet » est une competence, pas
    /// trois.
    /// </summary>
    public static HashSet<string> Termes(string? texte)
    {
        var sortie = new HashSet<string>(StringComparer.Ordinal);
        var plat = Aplatir(texte);
        if (plat.Length == 0) return sortie;

        // Les expressions d'abord, et on les retire du texte : sans cela
        // « permis b » laisserait derriere lui un « b » solitaire, et
        // « gestion de projet » un « gestion » et un « projet » qui
        // correspondraient a n'importe quelle annonce d'encadrement.
        foreach (var groupe in _synonymes)
        {
            foreach (var variante in groupe)
            {
                var v = Aplatir(variante);
                if (!v.Contains(' ')) continue;
                if (!plat.Contains(v, StringComparison.Ordinal)) continue;

                sortie.Add(_canon[v]);
                plat = plat.Replace(v, " ", StringComparison.Ordinal);
            }
        }

        foreach (var mot in plat.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (mot.Length < 2 || _motsVides.Contains(mot)) continue;
            sortie.Add(_canon.TryGetValue(mot, out var c) ? c : mot);
        }

        return sortie;
    }

    /// <summary>Replie un terme isole sur sa forme canonique.</summary>
    public static string Canon(string? terme)
    {
        var plat = Aplatir(terme);
        return _canon.TryGetValue(plat, out var c) ? c : plat;
    }

    // ══════════════════════════════════════
    //  Reconnaissance
    // ══════════════════════════════════════

    /// <summary>
    /// La famille de metiers a laquelle ce texte se rattache, ou null.
    ///
    /// On lui donne un intitule de poste, pas une description entiere :
    /// une annonce de comptable qui mentionne « notre logiciel » se
    /// rattacherait sinon au developpement.
    /// </summary>
    public static string? Metier(string? intitule)
    {
        var plat = Aplatir(intitule);
        if (plat.Length == 0) return null;

        // Les expressions sont deja triees du plus long au plus court : la
        // premiere qui correspond est la plus specifique.
        foreach (var (expression, nom) in _expressions)
            if (plat.Contains(expression, StringComparison.Ordinal))
                return nom;

        foreach (var mot in plat.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (_motUnique.TryGetValue(mot, out var nom))
                return nom;

        return null;
    }

    /// <summary>
    /// Le type de contrat que ce texte annonce, ou null.
    ///
    /// Sert a lire aussi bien le champ « ContractType » d'une offre — que
    /// chaque source d'import remplit a sa facon — qu'une requete tapee
    /// par un candidat.
    /// </summary>
    public static string? Contrat(string? texte)
    {
        var plat = Aplatir(texte);
        if (plat.Length == 0) return null;

        foreach (var (contrat, termes) in _contrats)
            foreach (var terme in termes)
                if (Regex.IsMatch(plat, $@"\b{Regex.Escape(Aplatir(terme))}\b"))
                    return contrat;

        return null;
    }

    /// <summary>
    /// Ce texte parle-t-il de travail a distance ?
    ///
    /// Le drapeau « IsRemote » existe sur l'offre mais n'est pose que par
    /// les depots faits sur le site : les offres importees le laissent a
    /// faux et disent « teletravail » dans leur texte.
    /// </summary>
    public static bool ParleDeDistance(string? texte)
    {
        var plat = Aplatir(texte);
        return plat.Length > 0 && Regex.IsMatch(
            plat, @"\b(teletravail|remote|a distance|distanciel|home office|full remote)\b");
    }

    /// <summary>
    /// Ce mot est-il trop courant pour distinguer quoi que ce soit ?
    ///
    /// Attend une forme deja aplatie. Sert a la recherche : passer
    /// « recherche », « poste » ou « equipe » a un « LIKE » sur la
    /// description ramene le catalogue entier, ce qui revient a ne pas
    /// filtrer tout en le faisant payer a la base.
    /// </summary>
    public static bool EstMotVide(string? motAplati) =>
        string.IsNullOrWhiteSpace(motAplati)
        || motAplati.Length < 2
        || _motsVides.Contains(motAplati);

    /// <summary>Toutes les familles connues, pour les ecrans de reglage et les tests.</summary>
    public static IReadOnlyList<string> Familles =>
        _familles.Select(f => f.Nom).ToList();
}
