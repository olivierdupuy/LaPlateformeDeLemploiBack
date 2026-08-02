using System.Globalization;
using System.Text.RegularExpressions;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Ce qu'un candidat cherche et que son profil ne dit pas.
///
/// « AppUser » porte un metier, des competences, une ville et des annees
/// d'experience — jamais le type de contrat vise ni le salaire attendu.
/// Ces souhaits-la vivent ailleurs : dans une recherche enregistree, dans
/// les filtres de la page en cours, dans la requete que le candidat vient
/// de taper. L'appelant les apporte quand il les connait ; les criteres
/// correspondants sont simplement ignores quand il ne les connait pas.
/// </summary>
public sealed record Souhaits(
    string? Contrat = null,
    bool? Distanciel = null,
    int? SalaireAnnuelMinimum = null,
    int? RayonKm = null);

/// <summary>
/// Le resultat d'une comparaison, et de quoi la justifier.
///
/// Un score nu ne vaut rien. « 78 % » n'apprend rien a un candidat et
/// n'autorise aucun recruteur a ecarter quelqu'un : ce sont les raisons
/// qui portent l'information, et les reserves qui evitent la mauvaise
/// surprise. Les deux listes sont donc rendues avec le chiffre, jamais
/// derriere une option.
/// </summary>
/// <param name="Score">De 0 a 100, sur les seuls criteres qu'on a pu juger.</param>
/// <param name="Fiabilite">
/// Quelle part de l'analyse a reellement pu etre menee, de 0 a 100. Une
/// offre qui ne dit ni son salaire, ni l'experience attendue, ni le
/// niveau de formation se juge sur trois criteres au lieu de sept : le
/// score reste calculable, mais il engage moins. C'est ce chiffre qui
/// permet a l'affichage de dire « correspondance estimee » plutot que
/// d'afficher un pourcentage peremptoire.
/// </param>
public sealed record Rapprochement(
    int Score,
    int Fiabilite,
    IReadOnlyList<string> Raisons,
    IReadOnlyList<string> Reserves);

/// <summary>
/// Est-ce que ce poste et cette personne vont ensemble, et pourquoi ?
///
/// La question etait deja posee — « GET /api/candidate/recommendations »
/// y repondait — mais mal : elle croisait les competences declarees avec
/// les etiquettes de l'offre, terme a terme, divisait par le nombre de
/// competences du candidat et ajoutait quinze points si le libelle du
/// lieu contenait le nom de la ville. Trois consequences :
///
///   Un profil complet etait penalise. Diviser par le nombre de
///   competences declarees fait qu'un candidat qui en saisit vingt
///   n'atteint jamais le seuil, quand celui qui en saisit deux le
///   depasse toujours. Le site punissait le soin.
///
///   Un profil sans competences saisies n'obtenait rien du tout — pas un
///   mauvais score, aucun resultat. C'est la majorite des inscrits.
///
///   La geographie se lisait par « Location.Contains(City) ». Un candidat
///   de Perpignan ne voyait rien a Canet-en-Roussillon, a onze
///   kilometres, et voyait tout a Paris des que l'annonce mentionnait
///   « Perpignan » dans son texte.
///
/// Ce service repond a la meme question par des criteres nommes, pesés,
/// et dont chacun sait dire ce qu'il a trouve. Il n'appelle aucun modele
/// de langage : le calcul doit tenir pour trois mille offres a chaque
/// recherche, et il doit tenir aussi quand la cle d'API manque.
/// </summary>
public static class Correspondance
{
    // ══════════════════════════════════════
    //  Les poids
    // ══════════════════════════════════════
    //
    // Ils sont ici, en clair, et leur somme fait cent. C'est le genre de
    // reglage qui derive en silence : une constante nommee se discute et
    // se teste, une valeur glissee dans une expression ne se retrouve
    // plus. Les tests les figent.
    //
    // Le metier et les competences pesent plus de la moitie a eux deux,
    // parce qu'ils repondent a la seule question qui rende les autres
    // interessantes. Le lieu vient ensuite : c'est le premier motif de
    // renoncement reel a une candidature.

    public const int PoidsMetier = 25;
    public const int PoidsCompetences = 30;
    public const int PoidsLieu = 20;
    public const int PoidsContrat = 10;
    public const int PoidsExperience = 8;
    public const int PoidsFormation = 4;
    public const int PoidsSalaire = 3;

    /// <summary>
    /// En deca, l'analyse a porte sur trop peu de criteres pour qu'on
    /// affiche un pourcentage sans le nuancer.
    /// </summary>
    public const int FiabiliteMinimale = 45;

    /// <summary>
    /// Duree legale annuelle du travail en France. Sert a ramener a
    /// l'annee un salaire annonce a l'heure : sans elle, un poste a
    /// 14 €/h et un poste a 22 500 €/an — le meme — ne se comparent pas.
    /// </summary>
    private const int HeuresParAn = 1607;

    // ══════════════════════════════════════
    //  Le profil, lu une seule fois
    // ══════════════════════════════════════

    /// <summary>
    /// Ce qu'on retient d'un candidat pour le comparer a des offres.
    ///
    /// Extrait une fois, compare mille fois : une recherche note le meme
    /// profil contre plusieurs milliers d'annonces, et refaire a chaque
    /// tour le decoupage des competences et le geocodage de la ville
    /// couterait plus cher que tout le reste du calcul.
    /// </summary>
    public sealed record Profil(
        HashSet<string> Competences,
        string? Metier,
        string? Ville,
        (double Lat, double Lng)? Position,
        int? Annees,
        int? Formation,
        Souhaits Souhaits);

    /// <summary>Prepare un profil a partir d'un compte et, si on les connait, de ses souhaits.</summary>
    public static Profil Lire(AppUser candidat, Souhaits? souhaits = null)
    {
        // L'intitule rejoint les competences : « Developpeur React » dit
        // « react », et beaucoup de profils remplissent l'un sans l'autre.
        var competences = LexiqueMetiers.Termes(
            $"{candidat.Skills} {candidat.Title}");

        return new Profil(
            competences,
            LexiqueMetiers.Metier(candidat.Title),
            candidat.City,
            GeoUtils.Geocode(candidat.City),
            candidat.ExperienceYears,
            NiveauFormation(candidat.Education),
            souhaits ?? new Souhaits());
    }

    // ══════════════════════════════════════
    //  La comparaison
    // ══════════════════════════════════════

    /// <summary>Note une offre pour un candidat, et dit pourquoi.</summary>
    public static Rapprochement Noter(AppUser candidat, JobOffer offre, Souhaits? souhaits = null) =>
        Noter(Lire(candidat, souhaits), offre);

    /// <summary>
    /// Note une offre pour un profil deja lu.
    ///
    /// Chaque critere rend une part de 0 a 1, ou rien du tout quand
    /// l'information manque des deux cotes. Un critere absent est retire
    /// du calcul au lieu d'etre compte comme nul : une annonce qui ne dit
    /// pas son salaire ne doit pas passer derriere une annonce qui le dit
    /// mal. C'est pour cela que le score se lit avec la fiabilite.
    /// </summary>
    public static Rapprochement Noter(Profil profil, JobOffer offre)
    {
        double points = 0;
        int poidsConnu = 0;
        var raisons = new List<string>();
        var reserves = new List<string>();

        void Peser(int poids, double? part)
        {
            if (part is null) return;
            poidsConnu += poids;
            points += poids * Math.Clamp(part.Value, 0, 1);
        }

        Peser(PoidsMetier, Metier(profil, offre, raisons, reserves));
        Peser(PoidsCompetences, Competences(profil, offre, raisons));
        Peser(PoidsLieu, Lieu(profil, offre, raisons, reserves));
        Peser(PoidsContrat, Contrat(profil, offre, raisons, reserves));
        Peser(PoidsExperience, Experience(profil, offre, raisons, reserves));
        Peser(PoidsFormation, Formation(profil, offre, reserves));
        Peser(PoidsSalaire, Salaire(profil, offre, raisons, reserves));

        if (poidsConnu == 0)
            return new Rapprochement(0, 0, Array.Empty<string>(), Array.Empty<string>());

        var total = PoidsMetier + PoidsCompetences + PoidsLieu + PoidsContrat
                    + PoidsExperience + PoidsFormation + PoidsSalaire;

        return new Rapprochement(
            (int)Math.Round(points / poidsConnu * 100),
            (int)Math.Round(poidsConnu / (double)total * 100),
            raisons,
            reserves);
    }

    // ── Metier ──

    private static double? Metier(Profil profil, JobOffer offre, List<string> raisons, List<string> reserves)
    {
        var vise = profil.Metier;
        var propose = LexiqueMetiers.Metier(offre.Title) ?? LexiqueMetiers.Metier(offre.Category);

        // Sans intitule exploitable d'un cote ou de l'autre, on ne juge
        // pas : le lexique ne connait pas tous les metiers, et repondre
        // « zero » pour un metier qu'il ignore reviendrait a punir le
        // candidat de son propre angle mort.
        if (vise is null || propose is null) return null;

        if (vise == propose)
        {
            raisons.Add($"métier : {propose.ToLowerInvariant()}");
            return 1;
        }

        // Neutre a dessein. Ces phrases sont rendues telles quelles au
        // candidat ET au recruteur qui classe ses candidatures : « autre
        // metier que le votre » se lisait de travers d'un cote sur deux.
        // Les seules tournures a la deuxieme personne qui subsistent sont
        // celles qui dependent des souhaits du candidat, et un recruteur
        // ne les connait pas — elles ne se declenchent jamais chez lui.
        reserves.Add($"autre métier : {propose.ToLowerInvariant()}");
        return 0;
    }

    // ── Competences ──

    private static double? Competences(Profil profil, JobOffer offre, List<string> raisons)
    {
        if (profil.Competences.Count == 0) return null;

        // On lit les champs structures, pas la description. Une annonce
        // dit « une bonne maitrise d'Excel serait un plus » et « nous
        // utilisons Slack » dans le meme paragraphe que « le candidat
        // rejoindra une equipe dynamique » : y pecher des competences
        // rapporte surtout du bruit, et le bruit gonfle tous les scores
        // en meme temps, ce qui revient a n'en avoir aucun.
        var demandees = LexiqueMetiers.Termes(
            $"{offre.Tags} {offre.Category} {offre.Title}");

        if (demandees.Count == 0) return null;

        var communes = profil.Competences.Intersect(demandees).ToList();

        // La reference est bornee entre trois et huit. En dessous de
        // trois, l'echantillon est trop maigre pour distinguer un bon
        // profil d'un profil chanceux. Au-dela de huit, on penaliserait
        // les annonces bavardes : une offre qui aligne trente etiquettes
        // deviendrait impossible a satisfaire, alors qu'en retrouver huit
        // est deja un signal fort.
        var reference = Math.Clamp(demandees.Count, 3, 8);
        var part = Math.Min(1.0, communes.Count / (double)reference);

        if (communes.Count > 0)
        {
            var citees = string.Join(", ", communes.Take(4));
            if (communes.Count > 4) citees += "…";
            raisons.Add(communes.Count == 1
                ? $"1 compétence en commun : {citees}"
                : $"{communes.Count} compétences en commun : {citees}");
        }

        return part;
    }

    // ── Lieu ──

    private static double? Lieu(Profil profil, JobOffer offre, List<string> raisons, List<string> reserves)
    {
        // Le drapeau « IsRemote » n'est pose que par les depots faits sur
        // le site. Les offres importees le laissent a faux et annoncent le
        // teletravail dans leur intitule ou leur champ « WorkplaceType ».
        var aDistance = offre.IsRemote
            || LexiqueMetiers.ParleDeDistance(offre.WorkplaceType)
            || LexiqueMetiers.ParleDeDistance(offre.Title);

        if (aDistance)
        {
            raisons.Add("télétravail possible");
            return 1;
        }

        // Un candidat qui veut expressement du distanciel et a qui l'on
        // propose du presentiel : le critere est tranche, pas absent.
        if (profil.Souhaits.Distanciel == true)
        {
            reserves.Add("poste sur site, alors que vous cherchez du télétravail");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(profil.Ville)) return null;

        var ici = profil.Position;
        var la = offre.Latitude.HasValue && offre.Longitude.HasValue
            ? (offre.Latitude.Value, offre.Longitude.Value)
            : GeoUtils.Geocode(offre.Location);

        if (ici is null || la is null)
        {
            // Faute de coordonnees des deux cotes, il reste le libelle.
            // C'est ce que faisait l'ancienne version — comme unique
            // moyen, c'etait insuffisant ; comme dernier recours, cela
            // rattrape les communes que le geocodeur ne connait pas.
            if (!string.IsNullOrWhiteSpace(offre.Location)
                && offre.Location.Contains(profil.Ville, StringComparison.OrdinalIgnoreCase))
            {
                raisons.Add($"à {profil.Ville}");
                return 1;
            }
            return null;
        }

        var km = GeoUtils.DistanceKm(ici.Value.Lat, ici.Value.Lng, la.Value.Item1, la.Value.Item2);

        // Un rayon explicite tranche : au-dela, le candidat a dit non.
        if (profil.Souhaits.RayonKm is int rayon && rayon > 0 && km > rayon)
        {
            reserves.Add($"à {Distance(km)}, hors du rayon de {rayon} km que vous avez fixé");
            return 0;
        }

        var part = km switch
        {
            <= 10 => 1.00,
            <= 30 => 0.85,
            <= 60 => 0.60,
            <= 100 => 0.35,
            _ => 0.10,
        };

        if (km <= 60) raisons.Add($"à {Distance(km)} de {profil.Ville}");
        else reserves.Add($"à {Distance(km)} de {profil.Ville}");

        return part;
    }

    private static string Distance(double km) =>
        km < 1 ? "moins d'un kilomètre" : $"{Math.Round(km)} km";

    /// <summary>
    /// Un montant en euros, ecrit a la francaise quelle que soit la
    /// machine.
    ///
    /// « {montant:N0} » suit la culture du processus : le meme code rend
    /// « 45 000 € » sur le serveur de production et « 45,000 € » sur un
    /// poste de developpement configure en anglais. Ces chaines partent
    /// telles quelles dans l'interface : la culture y est fixee, pas
    /// heritee.
    /// </summary>
    public static string Euros(int montant) =>
        montant.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) + " €";

    // ── Contrat ──

    private static double? Contrat(Profil profil, JobOffer offre, List<string> raisons, List<string> reserves)
    {
        var voulu = profil.Souhaits.Contrat;
        if (string.IsNullOrWhiteSpace(voulu)) return null;

        var propose = LexiqueMetiers.Contrat(offre.ContractType)
                      ?? LexiqueMetiers.Contrat(offre.Title);
        if (propose is null) return null;

        var attendu = LexiqueMetiers.Contrat(voulu) ?? voulu;

        if (string.Equals(propose, attendu, StringComparison.OrdinalIgnoreCase))
        {
            raisons.Add(propose);
            return 1;
        }

        reserves.Add($"{propose}, et vous cherchez un {attendu.ToLowerInvariant()}");
        return 0;
    }

    // ── Experience ──

    private static double? Experience(Profil profil, JobOffer offre, List<string> raisons, List<string> reserves)
    {
        if (profil.Annees is not int annees) return null;

        var exige = AnneesExigees(offre.ExperienceRequired);
        if (exige is not int minimum) return null;

        if (annees >= minimum)
        {
            // La surqualification ne coute pas de points : ce n'est pas au
            // site de decider qu'un poste est « en dessous » de quelqu'un.
            // Elle se signale, et chacun en fait ce qu'il veut.
            if (minimum <= 2 && annees >= minimum + 8)
                reserves.Add($"poste ouvert aux juniors, {annees} ans d'expérience déclarés");
            else if (minimum > 0)
                raisons.Add($"{minimum} ans d'expérience demandés, {annees} déclarés");

            return 1;
        }

        var manque = minimum - annees;
        reserves.Add($"{minimum} ans d'expérience demandés, {annees} déclarés");

        return manque switch
        {
            1 => 0.70,
            2 => 0.40,
            _ => 0.10,
        };
    }

    /// <summary>
    /// Le champ « ExperienceRequired » est un libelle, pas un nombre.
    /// On le ramene au plancher d'annees qu'il sous-entend.
    /// </summary>
    private static int? AnneesExigees(string? libelle)
    {
        var plat = LexiqueMetiers.Aplatir(libelle);
        if (plat.Length == 0) return null;

        // « 3 ans », « 3 a 5 ans », « minimum 5 ans » : le premier nombre
        // est le plancher.
        var m = Regex.Match(plat, @"\b(\d{1,2})\s*(ans?|annees?)\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;

        if (plat.Contains("debutant") || plat.Contains("junior") || plat.Contains("sans experience")) return 0;
        if (plat.Contains("intermediaire") || plat.Contains("confirme")) return 2;
        if (plat.Contains("senior") || plat.Contains("experimente")) return 5;
        if (plat.Contains("expert")) return 8;

        return null;
    }

    // ── Formation ──

    private static double? Formation(Profil profil, JobOffer offre, List<string> reserves)
    {
        if (profil.Formation is not int niveau) return null;

        var exige = NiveauFormation(offre.EducationLevel);
        if (exige is not int minimum) return null;

        if (niveau >= minimum) return 1;

        var manque = minimum - niveau;
        if (manque >= 2)
            reserves.Add($"formation attendue : {Diplome(minimum)}");

        return manque == 1 ? 0.6 : 0.2;
    }

    /// <summary>
    /// Un niveau de formation en annees apres le baccalaureat.
    ///
    /// Le champ de l'offre est normalise (« Bac+2 », « Bac+5 »), celui du
    /// candidat est libre : il y ecrit « Master 2 informatique », « BTS
    /// SIO », « Licence pro ». On lit les deux avec la meme echelle,
    /// faute de quoi le critere ne se declencherait jamais.
    /// </summary>
    private static int? NiveauFormation(string? texte)
    {
        var plat = LexiqueMetiers.Aplatir(texte);
        if (plat.Length == 0) return null;

        // « bac 5 » apres aplatissement de « Bac+5 » : le « + » a saute.
        var m = Regex.Match(plat, @"\bbac\s*(\d)\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n)) return n;

        if (plat.Contains("doctorat") || plat.Contains("these")) return 8;
        if (plat.Contains("master") || plat.Contains("ingenieur") || plat.Contains("mba")) return 5;
        if (plat.Contains("licence") || plat.Contains("bachelor")) return 3;
        if (plat.Contains("bts") || plat.Contains("dut") || plat.Contains("deug")) return 2;
        if (plat.Contains("cap") || plat.Contains("bep")) return -1;
        if (plat.Contains("bac")) return 0;

        return null;
    }

    private static string Diplome(int niveau) => niveau switch
    {
        <= -1 => "CAP ou BEP",
        0 => "baccalauréat",
        8 => "doctorat",
        _ => $"bac+{niveau}",
    };

    // ── Salaire ──

    private static double? Salaire(Profil profil, JobOffer offre, List<string> raisons, List<string> reserves)
    {
        if (profil.Souhaits.SalaireAnnuelMinimum is not int voulu || voulu <= 0) return null;

        var haut = AnnuelBrut(offre.MaxSalary ?? offre.MinSalary, offre.SalaryPeriod);
        if (haut is not int plafond || plafond <= 0) return null;

        if (plafond >= voulu)
        {
            raisons.Add($"jusqu'à {Euros(plafond)} par an");
            return 1;
        }

        reserves.Add($"jusqu'à {Euros(plafond)} par an, en deçà des {Euros(voulu)} visés");

        return plafond >= voulu * 0.9 ? 0.6 : 0.1;
    }

    /// <summary>
    /// Ramene un montant a l'annee.
    ///
    /// « MinSalary » et « MaxSalary » sont des entiers dont l'unite vit
    /// dans un autre champ, « SalaryPeriod ». Les comparer sans le lire
    /// met un poste a 14 €/h et un poste a 45 000 €/an sur la meme
    /// echelle, et le premier passe pour une misere.
    /// </summary>
    public static int? AnnuelBrut(int? montant, string? periode)
    {
        if (montant is not int m || m <= 0) return null;

        return LexiqueMetiers.Aplatir(periode) switch
        {
            "heure" or "horaire" or "h" => m * HeuresParAn,
            "mois" or "mensuel" or "m" => m * 12,
            "an" or "annuel" or "annee" or "" => m,
            // Unite inconnue : on se rabat sur l'ordre de grandeur, qui ne
            // trompe pas. Personne n'est paye 30 € par an, ni 60 000 € par
            // heure.
            _ => m < 200 ? m * HeuresParAn : m < 12_000 ? m * 12 : m,
        };
    }
}
