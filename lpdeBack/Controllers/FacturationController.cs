using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Formules, mises en avant et factures, cote recruteur.
///
/// Le paiement lui-meme n'est pas ici : il est derriere
/// <see cref="PrestatairePaiement"/>, qui repond 503 tant qu'aucune cle
/// n'est configuree — le meme parti que pour Brevo, OVH et le modele de
/// langage. Le reste du site continue de fonctionner sans, avec la
/// formule gratuite pour tout le monde ; c'est ce qui permet de livrer
/// la mecanique avant d'ouvrir un compte chez un prestataire.
/// </summary>
[ApiController]
[Route("api/facturation")]
[Authorize(Roles = "Recruiter,Admin")]
public class FacturationController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly FacturationService _facturation;
    private readonly PrestatairePaiement _paiement;
    private readonly ActivityLogService _activite;

    public FacturationController(
        AppDbContext context,
        FacturationService facturation,
        PrestatairePaiement paiement,
        ActivityLogService activite)
    {
        _context = context;
        _facturation = facturation;
        _paiement = paiement;
        _activite = activite;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Les formules et leur contenu. Ouvert : c'est une page de tarifs.</summary>
    [HttpGet("formules")]
    [AllowAnonymous]
    public IActionResult Formules() => Ok(Models.Formules.Toutes.Select(f => new
    {
        cle = f.Cle,
        nom = f.Nom,
        prixMensuelCentimes = f.PrixMensuelCentimes,
        offresActives = f.OffresActives,
        accesVivier = f.AccesVivier,
        misesEnAvantIncluses = f.MisesEnAvantIncluses,
        arguments = f.Arguments,
    }));

    /// <summary>Ou en est le recruteur : sa formule, ses quotas, ce qu'il lui reste.</summary>
    [HttpGet("mon-compte")]
    public async Task<IActionResult> MonCompte()
    {
        var formule = await _facturation.FormuleDe(UserId);
        var (peutPublier, motif, utilisees, quota) = await _facturation.PeutPublier(UserId);
        var restantes = await _facturation.MisesEnAvantRestantes(UserId);

        var abonnement = await _context.Abonnements
            .AsNoTracking()
            .Where(a => a.UserId == UserId && a.Statut == "actif")
            .OrderByDescending(a => a.DebutLe)
            .FirstOrDefaultAsync();

        return Ok(new
        {
            formule = new { formule.Cle, formule.Nom, formule.PrixMensuelCentimes },
            offres = new { utilisees, quota, peutPublier, motif },
            misesEnAvant = new
            {
                incluses = formule.MisesEnAvantIncluses,
                restantes,
                prixUnitaireCentimes = FacturationService.PrixMiseEnAvantCentimes,
                dureeJours = FacturationService.JoursMiseEnAvant,
            },
            abonnement = abonnement is null ? null : new { abonnement.DebutLe, abonnement.FinLe, abonnement.Statut },
            paiementDisponible = _paiement.EstConfigure,
        });
    }

    /// <summary>Les factures du recruteur, de la plus recente a la plus ancienne.</summary>
    [HttpGet("factures")]
    public async Task<IActionResult> Factures()
    {
        var factures = await _context.Factures
            .AsNoTracking()
            .Where(f => f.UserId == UserId)
            .OrderByDescending(f => f.EmiseLe)
            .Take(200)
            .ToListAsync();

        return Ok(factures);
    }

    /// <summary>
    /// Les mises en avant achetees ou consommees, avec l'offre qu'elles
    /// poussent. Sert de justificatif autant que de suivi.
    /// </summary>
    [HttpGet("mises-en-avant")]
    public async Task<IActionResult> MisesEnAvant()
    {
        var lignes = await _context.MisesEnAvant
            .AsNoTracking()
            .Where(m => m.UserId == UserId)
            .OrderByDescending(m => m.DebutLe)
            .Take(100)
            .Join(_context.JobOffers, m => m.JobOfferId, o => o.Id, (m, o) => new
            {
                m.Id,
                m.JobOfferId,
                offre = o.Title,
                m.DebutLe,
                m.FinLe,
                m.MontantCentimes,
                m.Origine,
                enCours = m.FinLe > DateTime.UtcNow,
            })
            .ToListAsync();

        return Ok(lignes);
    }

    public class DemandeMiseEnAvant
    {
        public int OffreId { get; set; }
    }

    /// <summary>
    /// Met une offre en avant.
    ///
    /// Trois issues : le quota de la formule la couvre et c'est fait ;
    /// il est epuise et le paiement est disponible, on rend l'adresse
    /// du tunnel ; il est epuise et aucun prestataire n'est configure,
    /// on le dit franchement plutot que de laisser un bouton qui ne
    /// fait rien.
    /// </summary>
    [HttpPost("mise-en-avant")]
    public async Task<IActionResult> Acheter([FromBody] DemandeMiseEnAvant demande)
    {
        var offre = await _context.JobOffers.FindAsync(demande.OffreId);
        if (offre is null) return NotFound(new { message = "Cette offre n'existe pas." });

        var estAdmin = User.IsInRole("Admin");
        if (!estAdmin && offre.CreatedByUserId != UserId)
            return Forbid();

        var restantes = await _facturation.MisesEnAvantRestantes(UserId);
        if (restantes > 0)
        {
            var mise = await _facturation.MettreEnAvant(UserId, offre.Id, payee: false, reference: null);
            await _activite.Log("mise_en_avant_incluse", "JobOffer", offre.Id,
                $"{offre.Title} — incluse dans la formule");

            return Ok(new
            {
                message = $"Offre mise en avant pour {FacturationService.JoursMiseEnAvant} jours (incluse dans votre formule).",
                finLe = mise!.FinLe,
                restantes = restantes - 1,
            });
        }

        if (!_paiement.EstConfigure)
            return StatusCode(503, new
            {
                message = "Vos mises en avant incluses sont epuisees et le paiement en ligne n'est pas encore ouvert. "
                        + "Contactez-nous pour en ajouter.",
            });

        var lien = await _paiement.CreerTunnel(
            UserId,
            $"Mise en avant — {offre.Title}",
            FacturationService.PrixMiseEnAvantCentimes,
            $"mise-en-avant:{offre.Id}");

        return Ok(new { redirection = lien });
    }

    public class DemandeAbonnement
    {
        public string Formule { get; set; } = "essentiel";
    }

    /// <summary>Souscrire une formule.</summary>
    [HttpPost("abonnement")]
    public async Task<IActionResult> Souscrire([FromBody] DemandeAbonnement demande)
    {
        var formule = Models.Formules.Par(demande.Formule);
        if (formule.Cle == "gratuit")
            return BadRequest(new { message = "La formule gratuite s'applique d'office." });

        if (!_paiement.EstConfigure)
            return StatusCode(503, new
            {
                message = "Le paiement en ligne n'est pas encore ouvert. Contactez-nous pour souscrire.",
            });

        var lien = await _paiement.CreerTunnel(
            UserId, $"Formule {formule.Nom}", formule.PrixMensuelCentimes, $"abonnement:{formule.Cle}");

        return Ok(new { redirection = lien });
    }

    /// <summary>
    /// Retour du prestataire une fois le paiement encaisse.
    ///
    /// Signee : sans cela, n'importe qui pourrait s'offrir une formule
    /// Pro en appelant cette adresse. La verification est faite par le
    /// prestataire, qui seul connait le format de sa signature.
    /// </summary>
    [HttpPost("retour-paiement")]
    [AllowAnonymous]
    public async Task<IActionResult> RetourPaiement()
    {
        using var lecteur = new StreamReader(Request.Body);
        var corps = await lecteur.ReadToEndAsync();

        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault()
                        ?? Request.Headers["X-Signature"].FirstOrDefault();

        var evenement = _paiement.LireRetour(corps, signature);
        if (evenement is null) return Unauthorized();
        if (!evenement.Paye) return NoContent();

        // Le motif dit ce qui a ete achete : c'est nous qui l'avons pose
        // a la creation du tunnel, le prestataire n'a fait que le rendre.
        var morceaux = evenement.Motif.Split(':', 2);
        var quoi = morceaux[0];
        var quoiId = morceaux.Length > 1 ? morceaux[1] : string.Empty;

        if (quoi == "mise-en-avant" && int.TryParse(quoiId, out var offreId))
        {
            await _facturation.MettreEnAvant(evenement.UserId, offreId, payee: true, reference: evenement.Reference);
            await _facturation.Emettre(evenement.UserId, $"Mise en avant d'offre #{offreId}",
                FacturationService.PrixMiseEnAvantCentimes, evenement.Reference);
        }
        else if (quoi == "abonnement")
        {
            var formule = Models.Formules.Par(quoiId);

            // Les formules precedentes se ferment : deux abonnements
            // actifs pour le meme compte donneraient le quota du dernier
            // enregistre, ce qui n'est pas une regle mais un hasard.
            var anciens = await _context.Abonnements
                .Where(a => a.UserId == evenement.UserId && a.Statut == "actif")
                .ToListAsync();
            foreach (var a in anciens) a.Statut = "annule";

            _context.Abonnements.Add(new Abonnement
            {
                UserId = evenement.UserId,
                Formule = formule.Cle,
                DebutLe = DateTime.UtcNow,
                FinLe = DateTime.UtcNow.AddMonths(1),
                Statut = "actif",
                ReferenceExterne = evenement.Reference,
            });

            await _context.SaveChangesAsync();
            await _facturation.Emettre(evenement.UserId, $"Formule {formule.Nom} — 1 mois",
                formule.PrixMensuelCentimes, evenement.Reference);
        }

        await _activite.Log("paiement_encaisse", "Facture", null,
            $"{evenement.Motif} — {evenement.MontantCentimes / 100.0:0.00} €");

        return NoContent();
    }

    // ── Administration ──

    /// <summary>Qui paie quoi. La vue d'ensemble manquait entierement.</summary>
    [HttpGet("recettes")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Recettes()
    {
        var debutDuMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var factures = await _context.Factures.AsNoTracking().ToListAsync();

        return Ok(new
        {
            totalHtCentimes = factures.Where(f => f.Statut != "annulee").Sum(f => f.MontantHtCentimes),
            moisHtCentimes = factures.Where(f => f.Statut != "annulee" && f.EmiseLe >= debutDuMois)
                                     .Sum(f => f.MontantHtCentimes),
            nombreFactures = factures.Count,
            abonnesActifs = await _context.Abonnements.CountAsync(a => a.Statut == "actif"),
            parFormule = await _context.Abonnements
                .Where(a => a.Statut == "actif")
                .GroupBy(a => a.Formule)
                .Select(g => new { formule = g.Key, nombre = g.Count() })
                .ToListAsync(),
            dernieres = factures.OrderByDescending(f => f.EmiseLe).Take(50),
        });
    }
}
