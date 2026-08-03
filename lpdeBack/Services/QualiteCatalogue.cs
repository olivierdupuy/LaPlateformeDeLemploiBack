using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Ce qui empeche le catalogue de se degrader.
///
/// Cinq sources alimentent la meme base — France Travail, Adzuna,
/// Jooble, Arbeitnow, Remotive. Trois problemes en decoulent, qu'aucun
/// import ne traitait :
///
///   **Les doublons.** « ExternalId » dedoublonne au sein d'une source,
///   pas entre elles : la meme annonce arrive avec trois identifiants
///   differents. Le candidat voit trois fois le meme poste, croit le
///   catalogue gonfle, et postule deux fois par erreur.
///
///   **Les morts.** Une offre importee qui n'apparait plus chez sa
///   source a ete retiree ou pourvue. Sans date de derniere vue, elle
///   reste en ligne pour toujours. Une offre morte coute plus cher
///   qu'une offre absente : elle fait perdre une candidature, puis la
///   confiance.
///
///   **Les arnaques.** La moderation etait entierement manuelle, donc
///   entierement en retard. Les signaux classiques — salaire aberrant,
///   contact hors plateforme, demande d'argent — se reconnaissent sans
///   modele de langage, et ce qui reste douteux part en file de
///   moderation plutot qu'a la poubelle : un blocage automatique sur un
///   signal statistique ecarterait aussi des annonces legitimes, et
///   personne ne le saurait.
/// </summary>
public class QualiteCatalogue
{
    private readonly AppDbContext _context;
    private readonly ILogger<QualiteCatalogue> _journal;

    /// <summary>
    /// Au-dela, l'offre part en moderation. Cale sur les signaux
    /// ci-dessous : deux signaux forts, ou un fort et deux faibles.
    /// </summary>
    public const int SeuilModeration = 60;

    /// <summary>
    /// Une offre importee non revue depuis ce delai est retiree. Trente
    /// jours : les agregateurs republient leurs annonces actives a
    /// chaque cycle, donc une annonce absente pendant un mois entier
    /// n'existe plus chez eux.
    /// </summary>
    public const int JoursAvantExpiration = 30;

    public QualiteCatalogue(AppDbContext context, ILogger<QualiteCatalogue> journal)
    {
        _context = context;
        _journal = journal;
    }

    // ══════════════════════════════════════
    //  Dedoublonnage
    // ══════════════════════════════════════

    /// <summary>
    /// L'empreinte d'un poste.
    ///
    /// Calculee sur ce qui identifie reellement une offre — intitule
    /// normalise, entreprise, ville — et non sur la description, que
    /// chaque agregateur reformate, tronque ou traduit. Deux annonces
    /// du meme poste chez deux agregateurs ont des descriptions
    /// differentes et le meme triplet.
    /// </summary>
    public static string Empreinte(string? titre, string? entreprise, string? lieu)
    {
        var cle = string.Join('|', Normaliser(titre), Normaliser(entreprise), NormaliserLieu(lieu));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cle)))[..32];
    }

    /// <summary>
    /// Minuscules et sans accents, mais la ponctuation reste.
    ///
    /// Distinct de <see cref="Normaliser"/>, qui efface aussi la
    /// ponctuation : l'analyse de fraude en a besoin — « carte
    /// d'identite » et « numero de securite sociale » perdraient leur
    /// forme, et les limites de mots des expressions ne tiendraient plus.
    /// </summary>
    private static string SansAccents(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return string.Empty;

        var decompose = valeur.Normalize(NormalizationForm.FormD);
        var sortie = new StringBuilder(decompose.Length);

        foreach (var c in decompose)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sortie.Append(c);

        // Les apostrophes typographiques viennent des traitements de
        // texte : « d’identite » n'est pas « d'identite » pour une
        // expression reguliere, et l'ecart suffit a tout laisser passer.
        return sortie.ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('’', '\'')
            .Replace('ʼ', '\'')
            .ToLowerInvariant();
    }

    /// <summary>
    /// Minuscules, sans accents, sans ponctuation, sans les mots vides
    /// qui varient d'une source a l'autre (« H/F », « CDI », « urgent »).
    /// </summary>
    private static string Normaliser(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return string.Empty;

        var sansAccents = new string(valeur
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        var texte = sansAccents.ToLowerInvariant();

        // Les mentions de genre et les etiquettes de contrat sont
        // ajoutees par les agregateurs, pas par l'employeur : les
        // garder ferait de « Developpeur H/F » et « Developpeur (h/f) »
        // deux postes distincts.
        texte = Regex.Replace(texte, @"\(?\s*[hf]\s*/\s*[hf]\s*\)?", " ");
        texte = Regex.Replace(texte, @"\b(cdi|cdd|stage|alternance|freelance|interim|urgent|nouveau)\b", " ");
        texte = Regex.Replace(texte, @"[^a-z0-9]+", " ");

        return string.Join(' ', texte.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Le lieu se decline : « 75 - Paris », « Paris 15e », « Paris
    /// (75) ». On ne garde que le nom, sans arrondissement ni code.
    /// </summary>
    private static string NormaliserLieu(string? lieu)
    {
        var texte = Normaliser(lieu);
        texte = Regex.Replace(texte, @"\b\d{1,5}\b", " ");
        texte = Regex.Replace(texte, @"\b\d+\s*(e|eme|er)\b", " ");
        return string.Join(' ', texte.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Cette offre existe-t-elle deja, sous un autre identifiant ?
    ///
    /// Rend l'offre deja presente, le cas echeant. L'appelant decide :
    /// a l'import on ignore la nouvelle, mais on met a jour la date de
    /// derniere vue de l'ancienne — c'est elle qui la maintient en vie.
    /// </summary>
    public async Task<JobOffer?> Doublon(string empreinte, string? sourceExclue = null)
    {
        return await _context.JobOffers
            .Where(o => o.Empreinte == empreinte && o.IsActive)
            .Where(o => sourceExclue == null || o.ExternalSource != sourceExclue)
            .OrderBy(o => o.Id)
            .FirstOrDefaultAsync();
    }

    // ══════════════════════════════════════
    //  Expiration
    // ══════════════════════════════════════

    /// <summary>
    /// Retire les offres importees qu'on n'a pas revues chez leur
    /// source depuis trop longtemps.
    ///
    /// Ne touche pas aux offres deposees sur le site : celles-la ont un
    /// proprietaire qui decide de leur sort, et les fermer d'office
    /// serait prendre une decision a sa place.
    /// </summary>
    public async Task<int> ExpirerLesImportees()
    {
        var limite = DateTime.UtcNow.AddDays(-JoursAvantExpiration);

        var perimees = await _context.JobOffers
            .Where(o => o.IsActive
                        && o.ExternalSource != null
                        && (o.VueChezLaSourceLe == null ? o.CreatedAt : o.VueChezLaSourceLe) < limite)
            .ToListAsync();

        if (perimees.Count == 0) return 0;

        // Une offre qui expire est fermee, pas suspendue : personne ne
        // l'a mise en pause, son affichage a simplement pris fin.
        foreach (var o in perimees) EtatOffre.Appliquer(o, false);
        await _context.SaveChangesAsync();

        _journal.LogInformation(
            "{Nombre} offres importees retirees : plus vues chez leur source depuis {Jours} jours",
            perimees.Count, JoursAvantExpiration);

        return perimees.Count;
    }

    // ══════════════════════════════════════
    //  Fraude
    // ══════════════════════════════════════

    /// <summary>Un signal releve, avec son poids.</summary>
    private record Signal(int Poids, string Motif);

    /// <summary>
    /// Note une offre de 0 a 100 et dit pourquoi.
    ///
    /// Sans appel a un modele de langage : ces signaux se reconnaissent
    /// par des regles, et une regle se lit, s'explique et ne coute rien
    /// a l'import de dix mille offres. L'analyse assistee est reservee
    /// aux cas que ces regles laissent en suspens.
    /// </summary>
    public (int Score, string? Motif) Analyser(JobOffer offre)
    {
        // ── Sans accents, et c'est indispensable ──
        //
        // Les motifs ci-dessous sont ecrits sans accents ; les annonces,
        // elles, en portent. « carte d'identité » ne declenchait donc
        // rien, et le signal le plus net du lot — la demande de piece
        // d'identite — passait a travers. Accentuer les motifs aurait
        // deplace le probleme : il aurait alors fallu prevoir les
        // annonces ecrites sans accents, qui existent aussi. On aplatit
        // le texte une fois, et les motifs restent lisibles.
        var texte = SansAccents((offre.Description ?? "") + " " + (offre.Title ?? ""));
        var signaux = new List<Signal>();

        // ── Deux signaux qui suffisent seuls ──
        //
        // Ils valent le seuil a eux seuls, et c'est delibere. Ils
        // pesaient d'abord 50 et 45, en dessous du seuil : chacun avait
        // alors besoin d'un signal faible pour declencher la
        // moderation. Une annonce reclamant des frais de dossier mais
        // par ailleurs bien redigee passait donc sans encombre, et les
        // tests ne l'avaient vu qu'en croisant les deux par accident.
        // Un signal quasi certain ne doit pas dependre d'un compagnon.

        // Demander de l'argent a un candidat est illegal en France
        // (article L5321-3 du code du travail). Il n'y a pas de cas
        // legitime a proteger.
        if (Regex.IsMatch(texte, @"\b(frais de dossier|frais d'inscription|versement|caution|acompte|payer pour|achat de kit)\b"))
            signaux.Add(new Signal(SeuilModeration, "demande d'argent au candidat"));

        // Les documents d'identite et les coordonnees bancaires se
        // demandent apres l'embauche, par le service du personnel, et
        // jamais dans une annonce.
        if (Regex.IsMatch(texte, @"\b(copie de (votre |la )?(carte d'identite|passeport)|rib|iban|numero de securite sociale)\b"))
            signaux.Add(new Signal(SeuilModeration, "demande de piece d'identite ou de coordonnees bancaires"));

        // ── Les suivants ont besoin d'un compagnon ──
        //
        // Pris isolement, chacun se rencontre dans des annonces
        // parfaitement honnetes. C'est leur accumulation qui compte.

        // Sortir du site des le premier contact est la manoeuvre
        // habituelle : elle met l'echange hors de toute trace.
        if (Regex.IsMatch(texte, @"\b(whatsapp|telegram|signal)\b"))
            signaux.Add(new Signal(35, "contact demande sur une messagerie privee"));

        if (Regex.IsMatch(texte, @"\b(gagnez|gagner) [0-9\s]+ ?(euros?|€) par (jour|semaine)\b")
            || Regex.IsMatch(texte, @"\bsans (experience|diplome|qualification) .{0,30}\b[0-9]{4,} ?(euros?|€)"))
            signaux.Add(new Signal(30, "promesse de gain disproportionnee"));

        // Un salaire tres au-dessus du marche sur un poste sans
        // exigence : pris seul, ce n'est qu'un signal faible — certains
        // metiers paient reellement beaucoup.
        if (offre.MaxSalary is > 200_000)
            signaux.Add(new Signal(20, "salaire annonce hors norme"));

        // Une annonce de trois lignes ne permet a personne de juger.
        if ((offre.Description?.Length ?? 0) < 120)
            signaux.Add(new Signal(15, "description tres courte"));

        // Les majuscules et les points d'exclamation en rafale sont la
        // signature du publipostage, pas d'un employeur.
        if (offre.Title is not null && offre.Title.Length > 12
            && offre.Title.Count(char.IsUpper) > offre.Title.Length * 0.6)
            signaux.Add(new Signal(10, "intitule tout en majuscules"));

        if (signaux.Count == 0) return (0, null);

        var score = Math.Min(100, signaux.Sum(s => s.Poids));
        return (score, string.Join(" · ", signaux.Select(s => s.Motif)));
    }

    /// <summary>
    /// Applique l'analyse a une offre et la met en file de moderation
    /// si le score le justifie. Rend vrai si l'offre a ete retenue.
    /// </summary>
    public bool Filtrer(JobOffer offre)
    {
        var (score, motif) = Analyser(offre);
        offre.ScoreFraude = score;
        offre.MotifFraude = motif;

        if (score < SeuilModeration) return false;

        offre.ModerationStatus = "Pending";
        _journal.LogWarning(
            "Offre retenue pour moderation (score {Score}) : {Motif}", score, motif);

        return true;
    }
}
