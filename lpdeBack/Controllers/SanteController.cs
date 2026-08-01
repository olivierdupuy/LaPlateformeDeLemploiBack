using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// L'etat de l'application, pour qui la surveille.
///
/// Rien ne prevenait quand quelque chose cassait : base injoignable,
/// tache de fond en exception, sauvegarde qui n'a plus tourne depuis
/// trois semaines — on l'apprenait par un visiteur.
///
/// Deux niveaux, et c'est delibere :
///
///   « /sante » est public et avare. Un service de surveillance a
///   besoin d'un code HTTP et d'un mot, pas de la liste de ce qui ne va
///   pas chez vous. Dire « la base est injoignable » a qui passe est un
///   renseignement offert.
///
///   « /sante/detail » dit tout, derriere l'authentification
///   administrateur.
/// </summary>
[ApiController]
[Route("api/sante")]
public class SanteController : ControllerBase
{
    /// <summary>Au-dela, la sauvegarde a trop vieilli.</summary>
    private static readonly TimeSpan SauvegardeTropVieille = TimeSpan.FromHours(36);

    /// <summary>Le demarrage : sert a ne pas s'alarmer des taches qui n'ont pas encore tourne.</summary>
    private static readonly DateTime Demarrage = DateTime.UtcNow;

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public SanteController(AppDbContext context, IConfiguration config, IWebHostEnvironment env)
    {
        _context = context;
        _config = config;
        _env = env;
    }

    /// <summary>
    /// Sain, degrade ou en panne — et rien de plus.
    ///
    /// 200 tant que le site rend service, 503 quand il ne le rend plus.
    /// « Degrade » reste a 200 : la lettre d'information peut dormir
    /// pendant que les visiteurs cherchent un emploi, et reveiller
    /// quelqu'un la nuit pour cela serait le meilleur moyen qu'on cesse
    /// de repondre aux alertes.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Etat()
    {
        var bilan = await Etablir();
        Response.Headers["Cache-Control"] = "no-store";
        return StatusCode(bilan.Vivant ? 200 : 503, new { etat = bilan.Etat });
    }

    /// <summary>Le detail, pour qui a le droit de le lire.</summary>
    [HttpGet("detail")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Detail()
    {
        var bilan = await Etablir();
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(new
        {
            etat = bilan.Etat,
            depuis = Demarrage,
            controles = bilan.Controles,
            taches = EtatDesServices.Rapport(DateTime.UtcNow - Demarrage),
        });
    }

    private sealed record Controle(string Quoi, string Etat, string Detail)
    {
        public bool Bloque => Etat == "en panne";
        public bool Gene => Etat == "dégradé";
    }

    private sealed record Bilan(string Etat, bool Vivant, IReadOnlyList<Controle> Controles);

    private async Task<Bilan> Etablir()
    {
        var controles = new List<Controle>
        {
            await Base(),
            Depot(),
            Sauvegarde(),
            Courriel(),
        };

        var taches = EtatDesServices.Rapport(DateTime.UtcNow - Demarrage);
        foreach (var t in taches.Where(t => t.Inquiete))
            controles.Add(new Controle($"tâche « {t.Service} »", "dégradé", t.Etat));

        var etat = controles.Any(c => c.Bloque) ? "en panne"
                 : controles.Any(c => c.Gene) ? "dégradé"
                 : "sain";

        return new Bilan(etat, !controles.Any(c => c.Bloque), controles);
    }

    /// <summary>
    /// La base repond-elle ? Une requete triviale suffit : ce qu'on
    /// verifie est la connexion, pas le contenu.
    /// </summary>
    private async Task<Controle> Base()
    {
        try
        {
            using var jeton = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _context.Database.ExecuteSqlRawAsync("SELECT 1", jeton.Token);
            return new Controle("base de données", "sain", "elle répond");
        }
        catch (Exception ex)
        {
            return new Controle("base de données", "en panne", ex.Message);
        }
    }

    /// <summary>
    /// La sauvegarde a-t-elle tourne, et a-t-elle reussi ?
    ///
    /// Le script nocturne depose « etat.json ». Son absence n'est pas
    /// une panne du site, mais c'est exactement ce qu'on veut voir
    /// avant d'en avoir besoin.
    /// </summary>
    private Controle Sauvegarde()
    {
        // Meme dossier commun que le script : hors de l'application, que
        // le deploiement ne remet pas a zero.
        var chemin = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LaPlateformeDeLemploi", "sauvegardes", "etat.json");
        if (!System.IO.File.Exists(chemin))
            return new Controle("sauvegarde", "dégradé", "aucune sauvegarde n'a jamais tourné");

        try
        {
            using var flux = System.IO.File.OpenRead(chemin);
            using var doc = JsonDocument.Parse(flux);
            var racine = doc.RootElement;

            var resultat = racine.TryGetProperty("resultat", out var r) ? r.GetString() : null;
            var quand = racine.TryGetProperty("quand", out var q) && q.TryGetDateTime(out var d)
                ? d.ToUniversalTime() : (DateTime?)null;
            var distant = racine.TryGetProperty("distant", out var x) && x.ValueKind == JsonValueKind.True;

            if (resultat != "reussi")
                return new Controle("sauvegarde", "dégradé",
                    "la dernière a échoué : " + (racine.TryGetProperty("detail", out var det) ? det.GetString() : "sans détail"));

            if (quand == null || DateTime.UtcNow - quand > SauvegardeTropVieille)
                return new Controle("sauvegarde", "dégradé",
                    $"la dernière remonte au {quand:dd/MM/yyyy HH:mm} — plus de 36 h");

            // Reussie et fraiche, mais restee sur le meme disque : elle
            // protege d'une fausse manoeuvre, pas d'un incendie.
            return distant
                ? new Controle("sauvegarde", "sain", $"du {quand:dd/MM/yyyy HH:mm}, copie hors du serveur")
                : new Controle("sauvegarde", "dégradé", $"du {quand:dd/MM/yyyy HH:mm}, mais aucune copie hors du serveur");
        }
        catch (Exception ex)
        {
            return new Controle("sauvegarde", "dégradé", "état illisible : " + ex.Message);
        }
    }

    /// <summary>
    /// Le rangement des CV est-il utilisable ?
    ///
    /// Il ne fait plus echouer le demarrage quand le serveur refuse
    /// d'ecrire — c'est ici qu'on l'apprend, au lieu de le decouvrir
    /// par un recruteur qui n'arrive plus a ouvrir un CV.
    /// </summary>
    private Controle Depot()
    {
        var depot = HttpContext.RequestServices.GetService<DepotFichiers>();
        if (depot == null) return new Controle("dépôt des CV", "dégradé", "non configuré");

        return depot.Empechement == null
            ? new Controle("dépôt des CV", "sain", depot.Racine)
            : new Controle("dépôt des CV", "dégradé", depot.Empechement);
    }

    /// <summary>De quoi envoyer ? Sans cela, aucun mot de passe oublie ne se retrouve.</summary>
    private Controle Courriel()
    {
        var brevo = _config["Brevo:ApiKey"];
        var smtp = _config["Smtp:Host"];
        return string.IsNullOrWhiteSpace(brevo) && string.IsNullOrWhiteSpace(smtp)
            ? new Controle("courriel", "dégradé", "aucun expéditeur configuré")
            : new Controle("courriel", "sain", string.IsNullOrWhiteSpace(brevo) ? "SMTP" : "Brevo");
    }
}
