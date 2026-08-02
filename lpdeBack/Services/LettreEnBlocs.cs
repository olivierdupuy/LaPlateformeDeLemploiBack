using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

// ══════════════════════════════════════════════════════════════
//  Une lettre faite de blocs
//
//  Le corps d'une campagne se saisissait dans un « textarea » de HTML
//  brut. Trois conséquences, toutes vérifiées sur les campagnes déjà
//  écrites :
//
//    On écrit du HTML à la main, le soir, sans filet. Une balise mal
//    fermée ne se voit qu'à l'aperçu, et parfois seulement dans une
//    messagerie sur les quatre.
//
//    Le HTML de courriel n'est pas celui d'une page. Outlook ignore la
//    grille et le flexbox, Gmail rogne les feuilles de style : il faut
//    des tableaux et des styles en ligne, que personne ne va écrire de
//    mémoire à onze heures du soir.
//
//    Et surtout : rien ne permettait de mettre une offre dans la lettre
//    d'un site d'emploi. Il fallait recopier un intitulé, une ville, un
//    lien, à la main, pour chaque annonce — donc on ne le faisait pas.
//
//  Les blocs répondent aux trois. Le rédacteur choisit un type, remplit
//  ce qui le concerne, et le rendu produit du HTML de courriel correct.
// ══════════════════════════════════════════════════════════════

/// <summary>Un bloc de la lettre.</summary>
public sealed class BlocLettre
{
    /// <summary>titre · texte · offres · bouton · separateur · image</summary>
    public string Type { get; set; } = "texte";

    public string? Texte { get; set; }

    /// <summary>Adresse du bouton, ou source de l'image.</summary>
    public string? Url { get; set; }

    /// <summary>gauche · centre</summary>
    public string? Alignement { get; set; }

    /// <summary>Texte de remplacement de l'image. Une image sans lui est invisible
    /// pour un lecteur d'écran, et pour toutes les messageries qui bloquent les
    /// images par défaut — c'est-à-dire la plupart.</summary>
    public string? Alt { get; set; }

    public BlocOffres? Offres { get; set; }
}

/// <summary>Le réglage d'un bloc d'offres.</summary>
public sealed class BlocOffres
{
    /// <summary>choisies · recherche · abonne</summary>
    public string Source { get; set; } = "abonne";

    /// <summary>Les offres piochées à la main, dans l'ordre voulu.</summary>
    public List<int>? Ids { get; set; }

    // ── Mode « recherche » ──
    public string? Metier { get; set; }
    public string? Lieu { get; set; }
    public string? Contrat { get; set; }
    public int? RayonKm { get; set; }

    public int Nombre { get; set; } = 5;

    /// <summary>
    /// Ce qu'on fait quand la sélection ne ramène rien.
    ///
    /// region · les offres récentes du département de l'abonné
    /// recentes · les offres récentes, sans condition de lieu
    /// masquer · le bloc disparaît
    ///
    /// Sans repli, un abonné dont le profil ne ramène rien reçoit une
    /// lettre trouée — un titre « Les offres près de chez vous » suivi de
    /// blanc. C'est pire que de ne pas lui écrire.
    /// </summary>
    public string Repli { get; set; } = "region";

    /// <summary>Un intertitre au-dessus des offres, facultatif.</summary>
    public string? Titre { get; set; }
}

/// <summary>
/// Les offres résolues une fois pour la campagne, réutilisées pour chaque
/// destinataire.
///
/// Un bloc « choisies » ou « recherche » donne la même chose à tout le
/// monde : le résoudre par abonné multiplierait la même requête par trois
/// mille. Seul le mode « abonné » varie, et il se calcule en mémoire sur
/// un vivier chargé une seule fois.
/// </summary>
public sealed class ContexteOffres
{
    /// <summary>Ce que chaque bloc, indexé par sa position, sert à tout le monde.</summary>
    public Dictionary<int, List<JobOffer>> ParBloc { get; init; } = new();

    /// <summary>Le vivier dans lequel le mode « abonné » puise, noté par profil.</summary>
    public List<JobOffer> Vivier { get; init; } = new();

    public static ContexteOffres Vide => new();
}

/// <summary>
/// Lire, écrire et rendre une lettre faite de blocs.
/// </summary>
public class LettreEnBlocs
{
    /// <summary>
    /// Combien d'offres on charge pour alimenter le mode « abonné ».
    ///
    /// Elles sont notées en mémoire, une fois par destinataire : le coût
    /// est celui d'un parcours de tableau, pas d'une requête. La borne
    /// existe pour ne pas charger un catalogue de cinquante mille lignes
    /// en RAM, pas parce que le calcul serait cher.
    /// </summary>
    public const int TailleVivier = 600;

    /// <summary>Au-delà, la lettre devient un catalogue et personne ne la lit.</summary>
    public const int MaxOffresParBloc = 12;

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public LettreEnBlocs(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    private string Site => (_config["App:PublicUrl"] ?? "").TrimEnd('/');

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ══════════════════════════════════════
    //  Lecture et écriture du JSON
    // ══════════════════════════════════════

    /// <summary>
    /// Les blocs d'une campagne, ou une liste vide.
    ///
    /// Tolérant par construction : une campagne écrite avant les blocs n'a
    /// pas de JSON, et une colonne abîmée ne doit pas empêcher d'ouvrir la
    /// campagne pour la réparer.
    /// </summary>
    public static List<BlocLettre> Lire(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<BlocLettre>();
        try
        {
            return JsonSerializer.Deserialize<List<BlocLettre>>(json, Json) ?? new List<BlocLettre>();
        }
        catch (JsonException)
        {
            return new List<BlocLettre>();
        }
    }

    public static string Ecrire(IEnumerable<BlocLettre> blocs) =>
        JsonSerializer.Serialize(blocs, Json);

    // ══════════════════════════════════════
    //  Résolution des offres
    // ══════════════════════════════════════

    /// <summary>
    /// Charge, une fois pour toute la campagne, ce dont les blocs d'offres
    /// auront besoin.
    ///
    /// À appeler avant la boucle d'envoi, jamais dedans.
    /// </summary>
    public async Task<ContexteOffres> Preparer(IReadOnlyList<BlocLettre> blocs, CancellationToken ct = default)
    {
        var contexte = new ContexteOffres();
        var besoinDeVivier = false;

        for (var i = 0; i < blocs.Count; i++)
        {
            var o = blocs[i].Offres;
            if (blocs[i].Type != "offres" || o is null) continue;

            var combien = Math.Clamp(o.Nombre, 1, MaxOffresParBloc);

            switch (o.Source)
            {
                case "choisies":
                    contexte.ParBloc[i] = await ParIdentifiants(o.Ids, ct);
                    break;

                case "recherche":
                    contexte.ParBloc[i] = await ParRecherche(o, combien, ct);
                    break;

                default:
                    besoinDeVivier = true;
                    break;
            }
        }

        // Le repli « région » et « récentes » puise aussi dans le vivier :
        // il faut donc le charger dès qu'un bloc peut se retrouver vide,
        // pas seulement quand un bloc est en mode « abonné ».
        var unRepliPeutServir = blocs.Any(b =>
            b.Type == "offres" && b.Offres is { } x && x.Repli != "masquer");

        if (besoinDeVivier || unRepliPeutServir)
        {
            var vivier = await Actives()
                .OrderByDescending(j => j.CreatedAt)
                .Take(TailleVivier)
                .ToListAsync(ct);

            return new ContexteOffres { ParBloc = contexte.ParBloc, Vivier = vivier };
        }

        return contexte;
    }

    private IQueryable<JobOffer> Actives() =>
        _context.JobOffers.AsNoTracking()
            .Where(j => j.IsActive && !j.IsDraft && j.ModerationStatus == "Approved");

    /// <summary>
    /// Les offres piochées à la main, dans l'ordre où le rédacteur les a
    /// mises — « WHERE Id IN (...) » les rendrait dans l'ordre de la base,
    /// ce qui déferait son classement sans qu'il comprenne pourquoi.
    /// </summary>
    private async Task<List<JobOffer>> ParIdentifiants(List<int>? ids, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return new List<JobOffer>();

        var voulues = ids.Take(MaxOffresParBloc).ToList();
        var trouvees = await Actives().Where(j => voulues.Contains(j.Id)).ToListAsync(ct);

        return voulues
            .Select(id => trouvees.FirstOrDefault(j => j.Id == id))
            .Where(j => j is not null)
            .Select(j => j!)
            .ToList();
    }

    /// <summary>
    /// Une sélection décrite plutôt qu'énumérée, résolue maintenant.
    ///
    /// C'est ce qui fait qu'une lettre préparée lundi et partie vendredi
    /// contient les offres de vendredi. Avec des identifiants figés, elle
    /// aurait pu annoncer des postes déjà pourvus.
    /// </summary>
    private async Task<List<JobOffer>> ParRecherche(BlocOffres o, int combien, CancellationToken ct)
    {
        var q = Actives();

        if (!string.IsNullOrWhiteSpace(o.Contrat))
        {
            var contrat = o.Contrat;
            q = q.Where(j => j.ContractType == contrat);
        }

        var centre = string.IsNullOrWhiteSpace(o.Lieu) ? null : GeoUtils.Trouver(o.Lieu);
        if (centre is null && !string.IsNullOrWhiteSpace(o.Lieu))
        {
            var lieu = o.Lieu;
            q = q.Where(j => j.Location.Contains(lieu));
        }

        // On charge large puis on affine en mémoire : le métier passe par
        // le lexique, que SQL ne sait pas appeler.
        var candidates = await q.OrderByDescending(j => j.CreatedAt).Take(TailleVivier).ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(o.Metier))
            candidates = candidates.Where(j => LexiqueMetiers.Metier(j.Title) == o.Metier).ToList();

        if (centre is not null)
        {
            var rayon = o.RayonKm is > 0 ? o.RayonKm.Value : 50;
            candidates = candidates
                .Where(j => j.Latitude.HasValue && j.Longitude.HasValue
                            && GeoUtils.DistanceKm(centre.Value.Lat, centre.Value.Lng,
                                                   j.Latitude.Value, j.Longitude.Value) <= rayon)
                .ToList();
        }

        return candidates.Take(combien).ToList();
    }

    /// <summary>
    /// Les offres de cet abonné-là.
    ///
    /// C'est ce qui sépare une lettre d'un site d'emploi d'un
    /// publipostage : la personne à Perpignan qui suit « Santé » reçoit
    /// des postes de soin près de chez elle, pas les mêmes six annonces
    /// que tout le monde. Tout est déjà là — la ville et les centres
    /// d'intérêt sont sur l'abonné, le moteur de rapprochement existe — il
    /// ne manquait que de les mettre ensemble.
    /// </summary>
    private List<JobOffer> PourLAbonne(NewsletterSubscriber a, ContexteOffres ctx, int combien)
    {
        if (ctx.Vivier.Count == 0) return new List<JobOffer>();

        // « Correspondance » lit un compte, pas un abonné. Un abonné n'a ni
        // compétences ni parcours : ses centres d'intérêt tiennent lieu des
        // deux, et c'est honnête — ce sont les seuls termes qu'il ait
        // lui-même déclarés.
        var profil = Correspondance.Lire(new AppUser
        {
            Title = a.Categories,
            Skills = a.Categories,
            City = a.City,
        });

        // Sans ville ni centre d'intérêt, il n'y a rien à rapprocher : le
        // repli fera mieux que des offres tirées au hasard.
        if (profil.Position is null && profil.Competences.Count == 0)
            return new List<JobOffer>();

        return ctx.Vivier
            .Select(j => new { Offre = j, Note = Correspondance.Noter(profil, j) })
            .Where(x => x.Note.Score >= 40)
            .OrderByDescending(x => x.Note.Score)
            .ThenByDescending(x => x.Offre.CreatedAt)
            .Take(combien)
            .Select(x => x.Offre)
            .ToList();
    }

    /// <summary>Le repli, quand la sélection n'a rien donné.</summary>
    private List<JobOffer> Repli(BlocOffres o, NewsletterSubscriber a, ContexteOffres ctx, int combien)
    {
        if (o.Repli == "masquer" || ctx.Vivier.Count == 0) return new List<JobOffer>();

        if (o.Repli == "region" && !string.IsNullOrWhiteSpace(a.Department))
        {
            var proches = ctx.Vivier
                .Where(j => NewsletterService.Departement(j.Location) == a.Department)
                .Take(combien)
                .ToList();

            // Un département sans offre ne vaut pas un bloc vide : on
            // élargit plutôt que de renoncer.
            if (proches.Count > 0) return proches;
        }

        return ctx.Vivier.Take(combien).ToList();
    }

    // ══════════════════════════════════════
    //  Rendu
    // ══════════════════════════════════════

    /// <summary>
    /// Le corps de la lettre pour un destinataire donné.
    ///
    /// Les champs de fusion ne sont pas remplacés ici : c'est
    /// « NewsletterService.Composer » qui s'en charge, sur l'ensemble
    /// assemblé, et qui échappe les valeurs au passage.
    /// </summary>
    public string Rendre(IReadOnlyList<BlocLettre> blocs, ContexteOffres ctx, NewsletterSubscriber a)
    {
        var sortie = new StringBuilder();

        for (var i = 0; i < blocs.Count; i++)
        {
            var b = blocs[i];
            sortie.Append(b.Type switch
            {
                "titre" => RendreTitre(b),
                "texte" => RendreTexte(b),
                "bouton" => RendreBouton(b),
                "separateur" => RendreSeparateur(),
                "image" => RendreImage(b),
                "offres" => RendreOffres(b, i, ctx, a),
                _ => string.Empty,
            });
        }

        return sortie.ToString();
    }

    /// <summary>
    /// Échappe ce qui est dangereux, et rien d'autre.
    ///
    /// « WebUtility.HtmlEncode » échappe aussi tout ce qui dépasse l'ASCII :
    /// « Développeur » en ressort « D&amp;#233;veloppeur », et une lettre
    /// écrite en français devient un mur d'entités. C'était utile quand un
    /// courriel pouvait arriver sans encodage déclaré ; l'enveloppe annonce
    /// de l'UTF-8 depuis le début, et les messageries le respectent.
    ///
    /// Les caractères qui comptent — &lt; &gt; &amp; guillemet apostrophe —
    /// restent échappés : c'est eux, et eux seuls, qui permettraient à un
    /// texte collé depuis un traitement de texte de devenir du balisage
    /// actif dans la boite de trois mille personnes.
    /// </summary>
    private static readonly HtmlEncoder Encodeur = HtmlEncoder.Create(UnicodeRanges.All);

    private static string E(string? v) => Encodeur.Encode(v ?? string.Empty);

    private static string Align(BlocLettre b) => b.Alignement == "centre" ? "center" : "left";

    private static string RendreTitre(BlocLettre b) =>
        string.IsNullOrWhiteSpace(b.Texte) ? "" : $"""
        <h2 style="margin:24px 0 10px;font-size:19px;line-height:1.35;font-weight:700;color:#001524;text-align:{Align(b)}">{E(b.Texte)}</h2>
        """;

    /// <summary>
    /// Un paragraphe par ligne vide.
    ///
    /// Le rédacteur écrit du texte, pas du balisage — il ne doit pas avoir
    /// à taper « &lt;p&gt; ». Tout est échappé : ce que quelqu'un colle
    /// depuis un traitement de texte ne doit pas pouvoir devenir du HTML
    /// actif dans la boite de trois mille personnes.
    /// </summary>
    private static string RendreTexte(BlocLettre b)
    {
        if (string.IsNullOrWhiteSpace(b.Texte)) return "";

        var paragraphes = b.Texte
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => E(p.Trim()).Replace("\n", "<br />"))
            .Where(p => p.Length > 0);

        return string.Concat(paragraphes.Select(p => $"""
            <p style="margin:0 0 14px;font-size:15px;line-height:1.7;color:#10272b;text-align:{Align(b)}">{p}</p>
            """));
    }

    private static string RendreBouton(BlocLettre b)
    {
        if (string.IsNullOrWhiteSpace(b.Texte) || string.IsNullOrWhiteSpace(b.Url)) return "";

        // Un bouton de courriel est un lien dans une cellule de tableau :
        // c'est la seule forme qu'Outlook rende correctement.
        return $"""
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:18px auto">
              <tr><td style="background:#15616d;border-radius:8px">
                <a href="{E(b.Url)}" style="display:inline-block;padding:12px 26px;font-size:15px;font-weight:600;color:#ffecd1;text-decoration:none">{E(b.Texte)}</a>
              </td></tr>
            </table>
            """;
    }

    private static string RendreSeparateur() => """
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
          <tr><td style="padding:14px 0"><div style="height:1px;background:#ebdac1;line-height:1px">&nbsp;</div></td></tr>
        </table>
        """;

    private static string RendreImage(BlocLettre b)
    {
        if (string.IsNullOrWhiteSpace(b.Url)) return "";

        // « alt » n'est jamais omis : une messagerie sur deux bloque les
        // images, et le texte de remplacement est alors tout ce qui reste.
        var img = $"""
            <img src="{E(b.Url)}" alt="{E(b.Alt)}" width="536"
                 style="display:block;width:100%;max-width:536px;height:auto;border-radius:10px;border:0" />
            """;

        return $"""
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
              <tr><td style="padding:8px 0" align="{Align(b)}">{img}</td></tr>
            </table>
            """;
    }

    // ── Les offres ──

    private string RendreOffres(BlocLettre b, int index, ContexteOffres ctx, NewsletterSubscriber a)
    {
        var o = b.Offres;
        if (o is null) return "";

        var combien = Math.Clamp(o.Nombre, 1, MaxOffresParBloc);

        var offres = o.Source switch
        {
            "choisies" or "recherche" => ctx.ParBloc.TryGetValue(index, out var l) ? l : new List<JobOffer>(),
            _ => PourLAbonne(a, ctx, combien),
        };

        if (offres.Count == 0) offres = Repli(o, a, ctx, combien);
        if (offres.Count == 0) return "";

        var titre = string.IsNullOrWhiteSpace(o.Titre) ? "" : $"""
            <h2 style="margin:24px 0 12px;font-size:19px;line-height:1.35;font-weight:700;color:#001524">{E(o.Titre)}</h2>
            """;

        var cartes = string.Concat(offres.Take(combien).Select(Carte));

        var voirTout = string.IsNullOrWhiteSpace(Site) ? "" : $"""
            <p style="margin:6px 0 0;text-align:center">
              <a href="{Site}/offres" style="font-size:13px;color:#15616d">Voir toutes les offres</a>
            </p>
            """;

        return titre + cartes + voirTout;
    }

    /// <summary>
    /// Une offre, en tableau.
    ///
    /// Ni grille ni flexbox : Outlook les ignore et empile tout à gauche.
    /// Les styles sont en ligne parce que Gmail supprime les feuilles de
    /// style d'en-tête.
    /// </summary>
    private string Carte(JobOffer j)
    {
        var lien = string.IsNullOrWhiteSpace(Site) ? "#" : $"{Site}/offres/{j.Id}";

        var faits = new List<string>();
        if (!string.IsNullOrWhiteSpace(j.Location)) faits.Add(E(j.Location));
        if (!string.IsNullOrWhiteSpace(j.ContractType)) faits.Add(E(j.ContractType));
        if (!string.IsNullOrWhiteSpace(j.Salary)) faits.Add(E(j.Salary));
        if (j.IsRemote) faits.Add("Télétravail");

        var ligne = faits.Count == 0 ? "" : $"""
            <p style="margin:4px 0 0;font-size:13px;color:#81999e">{string.Join(" · ", faits)}</p>
            """;

        return $"""
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="margin:0 0 10px">
              <tr><td style="padding:14px 16px;background:#fdf8f0;border:1px solid #ebdac1;border-radius:10px">
                <a href="{lien}" style="font-size:15px;font-weight:600;color:#001524;text-decoration:none">{E(j.Title)}</a>
                <p style="margin:2px 0 0;font-size:13px;color:#577177">{E(j.Company)}</p>
                {ligne}
              </td></tr>
            </table>
            """;
    }
}
