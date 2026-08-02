using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Controllers;

/// <summary>
/// Ce qui casse chez le visiteur, remonte jusqu'ici.
///
/// Une exception JavaScript ne laissait aucune trace : elle s'ecrivait
/// dans une console que personne n'ouvre, sur un appareil qu'on ne
/// possede pas. Le serveur, lui, repondait 200 a tout — un ecran blanc
/// pour tous les utilisateurs de Safari etait parfaitement invisible
/// depuis nos journaux.
///
/// Le depot est ouvert sans compte, ce qui en fait une porte : n'importe
/// qui peut y ecrire. D'ou trois garde-fous.
///
///   La limitation de debit, pour le volume.
///
///   Le regroupement par empreinte : mille remontees d'une meme faute
///   forment une ligne avec un compteur, pas mille lignes. C'est aussi
///   ce qui empeche de noyer la table.
///
///   La troncature systematique a l'ecriture. Le client tronque deja,
///   mais le client est le navigateur de quelqu'un d'autre : on ne lui
///   fait pas confiance pour la taille de ce qu'il envoie.
/// </summary>
[ApiController]
[Route("api/journal")]
public class JournalController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<JournalController> _journal;

    public JournalController(AppDbContext context, ILogger<JournalController> journal)
    {
        _context = context;
        _journal = journal;
    }

    public class ErreurEntrante
    {
        public string? Message { get; set; }
        public string? Pile { get; set; }
        public string? Chemin { get; set; }
        public string? Navigateur { get; set; }
    }

    /// <summary>
    /// Depot d'une erreur rencontree dans le navigateur.
    ///
    /// Repond 204 quoi qu'il arrive. Le client n'a rien a faire de la
    /// reponse — et surtout, un echec ici ne doit pas produire une
    /// nouvelle erreur cote navigateur, qui serait remontee a son tour.
    /// </summary>
    [HttpPost("erreur-navigateur")]
    [AllowAnonymous]
    [EnableRateLimiting("publication")]
    public async Task<IActionResult> Deposer([FromBody] ErreurEntrante entrante)
    {
        var message = Tronquer(entrante.Message, 500);
        if (string.IsNullOrWhiteSpace(message)) return NoContent();

        var pile = Tronquer(entrante.Pile, 4_000);
        var empreinte = Empreinte(message, pile);

        try
        {
            var existante = await _context.ErreursNavigateur
                .FirstOrDefaultAsync(e => e.Empreinte == empreinte);

            if (existante is not null)
            {
                existante.Occurrences++;
                existante.DerniereVue = DateTime.UtcNow;
                // Une faute qu'on croyait reglee et qui revient doit
                // ressortir de la liste : sinon on la classe une fois et
                // on ne la revoit plus jamais.
                existante.Traitee = false;
            }
            else
            {
                _context.ErreursNavigateur.Add(new ErreurNavigateur
                {
                    Message = message,
                    Pile = pile,
                    Chemin = Tronquer(entrante.Chemin, 500),
                    Navigateur = Tronquer(entrante.Navigateur, 300),
                    Empreinte = empreinte,
                });
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Deux navigateurs peuvent deposer la meme faute inedite en
            // meme temps et se heurter sur l'index unique. Ce n'est pas
            // un incident : on le note et on passe.
            _journal.LogWarning(ex, "Depot d'erreur navigateur impossible");
        }

        return NoContent();
    }

    /// <summary>Ce qui casse, du plus recent au plus ancien.</summary>
    [HttpGet("erreurs-navigateur")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Lister([FromQuery] bool traitees = false, [FromQuery] int limite = 100)
    {
        var lignes = await _context.ErreursNavigateur
            .Where(e => e.Traitee == traitees)
            .OrderByDescending(e => e.DerniereVue)
            .Take(Math.Clamp(limite, 1, 500))
            .Select(e => new
            {
                e.Id,
                e.Message,
                e.Pile,
                e.Chemin,
                e.Navigateur,
                e.Occurrences,
                e.PremiereVue,
                e.DerniereVue,
                e.Traitee,
            })
            .ToListAsync();

        return Ok(lignes);
    }

    /// <summary>
    /// La fraicheur du catalogue.
    ///
    /// L'import tournait toutes les six heures sans que personne ne
    /// puisse dire ce qu'il ramenait. On decouvrait qu'une source etait
    /// tombee en constatant que les offres dataient de trois semaines —
    /// et encore, en le cherchant.
    ///
    /// Ces chiffres repondent aux trois questions qu'on se pose quand on
    /// doute : qu'est-ce qui est en ligne, de quand cela date, et
    /// combien attendent une relecture.
    /// </summary>
    [HttpGet("catalogue")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Catalogue()
    {
        var maintenant = DateTime.UtcNow;

        var actives = _context.JobOffers.Where(o => o.IsActive && !o.IsDraft);

        var parSource = await actives
            .GroupBy(o => o.ExternalSource ?? "plateforme")
            .Select(g => new
            {
                source = g.Key,
                nombre = g.Count(),
                // La date de derniere vue chez la source ; a defaut,
                // celle de creation. Une offre deposee sur le site n'a
                // pas de source a revoir : c'est sa creation qui compte.
                plusRecente = g.Max(o => o.VueChezLaSourceLe ?? o.CreatedAt),
                plusAncienne = g.Min(o => o.VueChezLaSourceLe ?? o.CreatedAt),
            })
            .ToListAsync();

        // L'age median plutot que moyen : quelques offres tres vieilles
        // tirent une moyenne sans rien dire de l'etat general.
        var ages = await actives
            .Select(o => o.VueChezLaSourceLe ?? o.CreatedAt)
            .OrderBy(d => d)
            .ToListAsync();

        double? ageMedianJours = ages.Count == 0
            ? null
            : (maintenant - ages[ages.Count / 2]).TotalDays;

        var seuilExpiration = maintenant.AddDays(-lpdeBack.Services.QualiteCatalogue.JoursAvantExpiration);

        return Ok(new
        {
            total = await actives.CountAsync(),
            parSource = parSource.Select(s => new
            {
                s.source,
                s.nombre,
                s.plusRecente,
                s.plusAncienne,
                ageMoyenJours = Math.Round((maintenant - s.plusRecente).TotalDays, 1),
            }),
            ageMedianJours = ageMedianJours is null ? (double?)null : Math.Round(ageMedianJours.Value, 1),

            // Ce qui partira au prochain entretien : une valeur qui
            // grimpe signale une source qui ne repond plus.
            bientotExpirees = await actives.CountAsync(o =>
                o.ExternalSource != null && (o.VueChezLaSourceLe ?? o.CreatedAt) < seuilExpiration),

            enModeration = await _context.JobOffers
                .CountAsync(o => o.ModerationStatus == "Pending" && !o.IsDraft),

            // Les offres que l'analyse a retenues, du score le plus haut
            // au plus bas : c'est la file de travail du moderateur.
            suspectes = await _context.JobOffers
                .Where(o => o.ScoreFraude >= lpdeBack.Services.QualiteCatalogue.SeuilModeration)
                .OrderByDescending(o => o.ScoreFraude)
                .Take(20)
                .Select(o => new { o.Id, o.Title, o.Company, o.ScoreFraude, o.MotifFraude, o.ModerationStatus })
                .ToListAsync(),

            doublonsPotentiels = await actives
                .Where(o => o.Empreinte != null)
                .GroupBy(o => o.Empreinte)
                .CountAsync(g => g.Count() > 1),
        });
    }

    /// <summary>Classer une erreur. Elle sort de la liste sans etre effacee.</summary>
    [HttpPatch("erreurs-navigateur/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Classer(int id, [FromBody] Dictionary<string, bool> corps)
    {
        var ligne = await _context.ErreursNavigateur.FindAsync(id);
        if (ligne is null) return NotFound();

        ligne.Traitee = corps.TryGetValue("traitee", out var v) ? v : true;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static string? Tronquer(string? valeur, int longueur)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return null;
        valeur = valeur.Trim();
        return valeur.Length <= longueur ? valeur : valeur[..longueur];
    }

    /// <summary>
    /// Le message seul ne suffit pas : « Cannot read properties of
    /// undefined » designe cent fautes differentes. La premiere ligne
    /// utile de la pile les separe.
    /// </summary>
    private static string Empreinte(string message, string? pile)
    {
        var teteDePile = (pile ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .FirstOrDefault()?
            .Trim() ?? string.Empty;

        var octets = SHA256.HashData(Encoding.UTF8.GetBytes(message + "|" + teteDePile));
        return Convert.ToHexString(octets)[..32];
    }
}
