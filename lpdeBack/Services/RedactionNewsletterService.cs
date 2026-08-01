using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// La lettre d'information, redigee toute seule.
///
/// Un job hebdomadaire prepare une campagne par centre d'interet :
/// il choisit de vraies offres, demande a Claude d'ecrire ce qui les
/// relie, verifie qu'il n'a rien invente, et depose le tout en
/// brouillon. Il n'expedie jamais — un message parti ne se rattrape
/// pas, et la plateforme repond de ce qu'elle envoie. Le seul geste
/// humain qui reste est de lire et de cliquer.
///
/// ── Ce que l'IA n'a pas le droit de faire ──
///
/// Sur un site d'emploi, le contenu de la lettre EST de la donnee : les
/// offres parues. Une IA a qui l'on demanderait « redige une
/// newsletter » inventerait des intitules, des entreprises, des
/// salaires — c'est-a-dire annoncerait des postes qui n'existent pas,
/// ce qui engage la plateforme.
///
/// Le montage l'en empeche par construction plutot que par consigne :
/// le corps du message est assemble ICI, a partir des offres lues en
/// base. L'IA ne rend que du texte de liaison, rattache a des
/// identifiants qu'on lui a donnes. Une offre qu'elle inventerait n'a
/// nulle part ou s'afficher, et un identifiant hors corpus est rejete.
/// </summary>
public class RedactionNewsletterService : BackgroundService
{
    /// <summary>On repasse souvent, on n'agit que le jour dit.</summary>
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(6);

    /// <summary>En deca, la lettre n'a rien a raconter et ne part pas.</summary>
    private const int OffresMinimum = 3;

    /// <summary>Au-dela, on n'ajoute que du bruit.</summary>
    private const int OffresMaximum = 8;

    /// <summary>Sur quelle profondeur on regarde ce qui est paru.</summary>
    private static readonly TimeSpan Fenetre = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _fabrique;
    private readonly ILogger<RedactionNewsletterService> _log;

    public RedactionNewsletterService(IServiceScopeFactory fabrique,
                                      ILogger<RedactionNewsletterService> log)
    {
        _fabrique = fabrique;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        // Laisser l'application finir de demarrer : migrations, amorcage.
        try { await Task.Delay(TimeSpan.FromSeconds(40), arret); }
        catch (OperationCanceledException) { return; }

        using var horloge = new PeriodicTimer(Intervalle);
        do
        {
            try { await Preparer(arret); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _log.LogError(ex, "Redaction automatique de la lettre : echec du tour"); }
        }
        while (await horloge.WaitForNextTickAsync(arret));
    }

    private async Task Preparer(CancellationToken ct)
    {
        using var portee = _fabrique.CreateScope();
        var db = portee.ServiceProvider.GetRequiredService<AppDbContext>();
        var ia = portee.ServiceProvider.GetRequiredService<AiClient>();

        var actif = await db.PlatformSettings
            .Where(s => s.Key == "newsletter_auto_redaction")
            .Select(s => s.Value).FirstOrDefaultAsync(ct) == "true";
        if (!actif) return;

        if (!ia.IsConfigured)
        {
            _log.LogWarning("Redaction automatique demandee, mais aucun modele d'IA n'est configure.");
            return;
        }

        // Une seule preparation par semaine : le job repasse toutes les six
        // heures, il ne doit pas deposer quatre brouillons par jour.
        var depuis = DateTime.UtcNow - Fenetre;
        var dejaPrepare = await db.NewsletterCampaigns
            .AnyAsync(c => c.CreatedByName == Signature && c.CreatedAt >= depuis, ct);
        if (dejaPrepare) return;

        // Les centres d'interet reellement choisis par des abonnes
        // confirmes : preparer une lettre « Design » sans personne pour la
        // lire couterait des jetons pour rien.
        var abonnes = await db.NewsletterSubscribers
            .Where(a => a.Status == "Confirmed" && a.UnsubscribedAt == null)
            .Select(a => a.Categories)
            .ToListAsync(ct);

        var categories = abonnes
            .SelectMany(c => (c ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries
                                                | StringSplitOptions.TrimEntries))
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        if (categories.Count == 0)
        {
            _log.LogInformation("Redaction automatique : aucun centre d'interet choisi, rien a preparer.");
            return;
        }

        foreach (var categorie in categories)
        {
            if (ct.IsCancellationRequested) return;
            try { await PreparerUne(db, ia, categorie, ct); }
            catch (Exception ex)
            {
                // Une categorie qui echoue n'empeche pas les autres : la
                // lettre « Tech » ne doit pas tomber parce que « Design »
                // a rate.
                _log.LogError(ex, "Redaction automatique : echec sur « {Categorie} »", categorie);
            }
        }
    }

    /// <summary>Ce qui distingue une campagne redigee d'une campagne ecrite a la main.</summary>
    public const string Signature = "Rédaction automatique";

    private async Task PreparerUne(AppDbContext db, AiClient ia, string categorie, CancellationToken ct)
    {
        // ── 1. Le corpus : de vraies offres, et rien d'autre ──
        var depuis = DateTime.UtcNow - Fenetre;
        var offres = await db.JobOffers
            .Where(o => o.IsActive && !o.IsDraft && o.ModerationStatus == "Approved"
                        && o.Category == categorie && o.CreatedAt >= depuis)
            .OrderByDescending(o => o.CreatedAt)
            .Take(OffresMaximum)
            .Select(o => new OffreCorpus(o.Id, o.Title, o.Company, o.Location,
                                         o.ContractType, o.Salary))
            .ToListAsync(ct);

        if (offres.Count < OffresMinimum)
        {
            _log.LogInformation("Redaction automatique : « {Categorie} » n'a que {N} offre(s), on passe.",
                                categorie, offres.Count);
            return;
        }

        // ── 2. L'IA n'ecrit que le liant ──
        var redaction = await Rediger(ia, categorie, offres, ct);
        if (redaction == null) return;

        // ── 3. Ce qu'elle a cite doit exister ──
        var connus = offres.Select(o => o.Id).ToHashSet();
        var inventees = redaction.Offres.Where(o => !connus.Contains(o.Id)).ToList();
        if (inventees.Count > 0)
        {
            _log.LogWarning("Redaction automatique : « {Categorie} » citait {N} offre(s) hors corpus, campagne abandonnee.",
                            categorie, inventees.Count);
            return;
        }

        // Une accroche ne contient ni lien ni adresse : c'est du texte de
        // liaison, et un lien invente menerait n'importe ou.
        if (redaction.Accroche.Contains("http", StringComparison.OrdinalIgnoreCase)
            || redaction.Accroche.Contains('@'))
        {
            _log.LogWarning("Redaction automatique : « {Categorie} » a produit un lien dans l'accroche, campagne abandonnee.",
                            categorie);
            return;
        }

        // ── 4. Le corps est assemble ici, a partir des offres lues ──
        var corps = Assembler(redaction, offres);

        db.NewsletterCampaigns.Add(new NewsletterCampaign
        {
            Subject = redaction.Objet,
            PreviewText = redaction.Accroche.Length > 180
                ? redaction.Accroche[..180] : redaction.Accroche,
            BodyHtml = corps,
            Status = "Draft",
            SegmentCategories = categorie,
            CreatedByName = Signature,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        _log.LogInformation("Redaction automatique : brouillon depose pour « {Categorie} » ({N} offres).",
                            categorie, offres.Count);
    }

    // ══════════════════════════════════════
    //  L'appel au modele
    // ══════════════════════════════════════

    private record OffreCorpus(int Id, string Titre, string Entreprise,
                               string Lieu, string Contrat, string? Salaire);

    private record PhraseOffre(int Id, string Phrase);
    private record Redaction(string Objet, string Accroche, List<PhraseOffre> Offres);

    private async Task<Redaction?> Rediger(AiClient ia, string categorie,
                                           List<OffreCorpus> offres, CancellationToken ct)
    {
        var liste = string.Join("\n", offres.Select(o =>
            $"- id {o.Id} | {o.Titre} | {o.Entreprise} | {o.Lieu} | {o.Contrat}"
            + (string.IsNullOrWhiteSpace(o.Salaire) ? "" : $" | {o.Salaire}")));

        var consigne = $$"""
            Tu rediges le liant d'une lettre d'information pour un site d'emploi
            francais, sur le theme « {{categorie}} ».

            Tu ne disposes que des offres ci-dessous. Tu n'en inventes AUCUNE, tu
            n'inventes ni salaire, ni entreprise, ni lieu, ni lien. Tu ne cites que
            des identifiants presents dans cette liste.

            {{liste}}

            Rends un JSON strict, sans texte autour :
            {
              "objet": "objet du courriel, 60 caracteres au plus, sans emoji",
              "accroche": "deux phrases qui donnent envie de lire, sans lien ni adresse",
              "offres": [ { "id": <identifiant>, "phrase": "une phrase sur cette offre, 140 caracteres au plus" } ]
            }

            La phrase de chaque offre dit ce qu'elle a de notable — le contrat, le
            lieu, la remuneration si elle est donnee. Elle ne repete pas l'intitule.
            Tu ecris sobrement : pas de superlatif, pas de point d'exclamation.
            """;

        var r = await ia.ChatAsync(
            "Tu es redacteur pour un site d'emploi francais. Tu ecris sobrement et tu "
            + "n'inventes jamais un fait. Tu reponds UNIQUEMENT avec un JSON valide.",
            consigne, temperature: 0.6, maxTokens: 1500, cancellationToken: ct);

        if (!r.Ok || string.IsNullOrWhiteSpace(r.Content))
        {
            _log.LogWarning("Redaction automatique : le modele n'a pas repondu pour « {Categorie} » ({Erreur})",
                            categorie, r.Error);
            return null;
        }

        try
        {
            var brut = r.Content.Trim();
            // Les modeles encadrent volontiers leur JSON de balises de code.
            var debut = brut.IndexOf('{');
            var fin = brut.LastIndexOf('}');
            if (debut < 0 || fin <= debut) return null;
            brut = brut[debut..(fin + 1)];

            using var doc = JsonDocument.Parse(brut);
            var racine = doc.RootElement;
            var objet = racine.GetProperty("objet").GetString() ?? "";
            var accroche = racine.GetProperty("accroche").GetString() ?? "";
            var phrases = new List<PhraseOffre>();
            if (racine.TryGetProperty("offres", out var tab) && tab.ValueKind == JsonValueKind.Array)
                foreach (var e in tab.EnumerateArray())
                    if (e.TryGetProperty("id", out var id) && e.TryGetProperty("phrase", out var p))
                        phrases.Add(new PhraseOffre(id.GetInt32(), p.GetString() ?? ""));

            if (string.IsNullOrWhiteSpace(objet) || phrases.Count == 0) return null;
            return new Redaction(objet.Trim(), accroche.Trim(), phrases);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Redaction automatique : reponse illisible pour « {Categorie} »", categorie);
            return null;
        }
    }

    // ══════════════════════════════════════
    //  L'assemblage
    // ══════════════════════════════════════

    /// <summary>
    /// Le corps est bati a partir des offres lues en base.
    ///
    /// C'est ce qui rend l'invention impossible plutot qu'improbable :
    /// une offre absente du corpus n'a aucun emplacement ou paraitre. La
    /// phrase du modele vient s'y poser, echappee.
    /// </summary>
    private static string Assembler(Redaction r, List<OffreCorpus> offres)
    {
        var par = offres.ToDictionary(o => o.Id);
        var blocs = new List<string>();

        foreach (var p in r.Offres)
        {
            if (!par.TryGetValue(p.Id, out var o)) continue;
            var details = string.Join(" &middot; ", new[]
            {
                E(o.Lieu), E(o.Contrat), string.IsNullOrWhiteSpace(o.Salaire) ? null : E(o.Salaire!),
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

            blocs.Add($"""
                <tr><td style="padding:0 0 18px">
                  <div style="border:1px solid #ebdac1;border-radius:10px;padding:14px 16px">
                    <div style="font-size:15px;font-weight:600;color:#10272b">{E(o.Titre)}</div>
                    <div style="font-size:13px;color:#39545a;margin-top:2px">{E(o.Entreprise)}</div>
                    <div style="font-size:12px;color:#81999e;margin-top:6px">{details}</div>
                    <div style="font-size:13px;color:#39545a;margin-top:8px;line-height:1.55">{E(p.Phrase)}</div>
                  </div>
                </td></tr>
                """);
        }

        // Le champ de fusion vit hors du litteral : ses accolades se
        // heurteraient a l'interpolation, et l'echapper rendrait la chaine
        // illisible pour qui la relit.
        const string champPrenom = "{{prenom}}";

        return $"""
            <p>Bonjour {champPrenom},</p>
            <p>{E(r.Accroche)}</p>
            <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
              {string.Join("\n", blocs)}
            </table>
            <p style="font-size:13px;color:#81999e">
              Ces offres etaient en ligne au moment de l'envoi. Certaines peuvent
              avoir ete pourvues depuis.
            </p>
            """;
    }

    private static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
}
