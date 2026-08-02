using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Ce que le site encaisse, vu de la plateforme.
///
/// La facturation existe, elle est reelle et testee — mais elle repond au
/// recruteur sur son propre compte. La console n'en montrait qu'un total,
/// glisse dans la page d'exploitation. Personne ne pouvait repondre a
/// « qui paie », « quelle facture est restee impayee », « quelle mise en
/// avant tourne encore et jusqu'a quand » : les trois questions qu'on pose
/// le jour ou un client appelle.
///
/// Une regle traverse tout ce fichier : **rien ici ne debite personne**.
/// La relance est un courriel. Le marquage « payee » enregistre un
/// virement deja recu. Un prelevement declenche depuis un ecran
/// d'administration serait un debit qu'aucun client n'a autorise ce
/// jour-la, et le journal ne dirait pas pourquoi.
/// </summary>
[ApiController]
[Route("api/admin/finances")]
[Authorize(Roles = "Admin")]
public class AdminFinancesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ActivityLogService _log;
    private readonly IEmailSender _mail;
    private readonly IConfiguration _config;

    public AdminFinancesController(AppDbContext context, ActivityLogService log,
                                   IEmailSender mail, IConfiguration config)
    {
        _context = context;
        _log = log;
        _mail = mail;
        _config = config;
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private string NomComplet() =>
        $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}".Trim();
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string Site => (_config["App:PublicUrl"] ?? "").TrimEnd('/');

    /// <summary>
    /// A partir de quand une echeance merite d'etre signalee.
    ///
    /// Trente jours : le temps de joindre un client, de lui laisser
    /// changer d'avis, et de le relancer une fois. Plus court, on
    /// decouvre l'echeance le jour ou elle tombe.
    /// </summary>
    public const int JoursAvantEcheance = 30;

    // ══════════════════════════════════════
    //  La vue d'ensemble
    // ══════════════════════════════════════

    [HttpGet("resume")]
    public async Task<ActionResult<object>> Resume(CancellationToken ct)
    {
        var maintenant = DateTime.UtcNow;
        var douzeMois = new DateTime(maintenant.Year, maintenant.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(-11);

        // ── Les recettes, mois par mois ──
        //
        // Sur les factures payees et non sur les factures emises : une
        // facture emise n'est pas une recette, c'est une esperance, et
        // les confondre donne un chiffre d'affaires qui n'existe pas.
        var payees = await _context.Factures
            .AsNoTracking()
            .Where(f => f.Statut == "payee" && f.PayeeLe != null && f.PayeeLe >= douzeMois)
            .Select(f => new { f.PayeeLe, f.MontantTtcCentimes, f.MontantHtCentimes })
            .ToListAsync(ct);

        var mois = Enumerable.Range(0, 12)
            .Select(i => douzeMois.AddMonths(i))
            .Select(m => new
            {
                mois = m.ToString("yyyy-MM"),
                ttcCentimes = payees.Where(f => f.PayeeLe!.Value.Year == m.Year
                                             && f.PayeeLe.Value.Month == m.Month)
                                    .Sum(f => f.MontantTtcCentimes),
                htCentimes = payees.Where(f => f.PayeeLe!.Value.Year == m.Year
                                            && f.PayeeLe.Value.Month == m.Month)
                                   .Sum(f => f.MontantHtCentimes),
            })
            .ToList();

        // ── Les abonnements ──
        var abonnements = await _context.Abonnements
            .AsNoTracking()
            .Where(a => a.Statut != "annule")
            .Select(a => new { a.Formule, a.Statut, a.FinLe })
            .ToListAsync(ct);

        var parFormule = Formules.Toutes
            .Select(f => new
            {
                cle = f.Cle,
                nom = f.Nom,
                prixMensuelCentimes = f.PrixMensuelCentimes,
                nombre = abonnements.Count(a => a.Formule == f.Cle && a.Statut == "actif"),
            })
            .ToList();

        var echeance = maintenant.AddDays(JoursAvantEcheance);

        return Ok(new
        {
            recettes = mois,
            parFormule,
            // Le revenu mensuel recurrent : ce que les abonnements actifs
            // rapporteront le mois prochain si personne ne part. Il se
            // distingue des recettes, qui sont ce qui est deja rentre.
            recurrentMensuelCentimes = parFormule.Sum(f => f.nombre * f.prixMensuelCentimes),
            abonnementsActifs = abonnements.Count(a => a.Statut == "actif"),
            abonnementsImpayes = abonnements.Count(a => a.Statut == "impaye"),
            echeancesProches = abonnements.Count(a => a.Statut == "actif"
                                                   && a.FinLe != null
                                                   && a.FinLe <= echeance
                                                   && a.FinLe >= maintenant),
            facturesImpayees = await _context.Factures.CountAsync(f => f.Statut == "emise", ct),
            misesEnAvantActives = await _context.MisesEnAvant
                .CountAsync(m => m.FinLe > maintenant, ct),
        });
    }

    // ══════════════════════════════════════
    //  Les abonnements
    // ══════════════════════════════════════

    [HttpGet("abonnements")]
    public async Task<ActionResult<object>> Abonnements(CancellationToken ct)
    {
        var maintenant = DateTime.UtcNow;
        var echeance = maintenant.AddDays(JoursAvantEcheance);

        var lignes = await _context.Abonnements
            .AsNoTracking()
            .Where(a => a.Statut != "annule")
            .Join(_context.Users, a => a.UserId, u => u.Id, (a, u) => new
            {
                a.Id, a.Formule, a.Statut, a.DebutLe, a.FinLe,
                a.Entreprise,
                compte = u.Email,
                nom = (u.FirstName + " " + u.LastName).Trim(),
            })
            .ToListAsync(ct);

        // Ce qui demande une action d'abord : l'impaye, puis l'echeance
        // proche, puis le reste par date.
        var sortie = lignes
            .Select(a => new
            {
                a.Id, a.Formule, a.Statut, a.DebutLe, a.FinLe, a.Entreprise, a.compte, a.nom,
                formuleNom = Formules.Par(a.Formule).Nom,
                prixMensuelCentimes = Formules.Par(a.Formule).PrixMensuelCentimes,
                expireBientot = a.Statut == "actif" && a.FinLe != null
                             && a.FinLe <= echeance && a.FinLe >= maintenant,
                expire = a.FinLe != null && a.FinLe < maintenant,
            })
            .OrderByDescending(a => a.Statut == "impaye")
            .ThenByDescending(a => a.expireBientot)
            .ThenBy(a => a.FinLe ?? DateTime.MaxValue)
            .ToList();

        return Ok(sortie);
    }

    // ══════════════════════════════════════
    //  Les factures
    // ══════════════════════════════════════

    /// <param name="statut">« emise » (impayee), « payee », « annulee ». Toutes par defaut.</param>
    [HttpGet("factures")]
    public async Task<ActionResult<object>> Factures([FromQuery] string? statut, CancellationToken ct)
    {
        var q = _context.Factures.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(statut)) q = q.Where(f => f.Statut == statut);

        var lignes = await q
            .OrderByDescending(f => f.EmiseLe)
            .Take(300)
            .Join(_context.Users, f => f.UserId, u => u.Id, (f, u) => new
            {
                f.Id, f.Numero, f.Libelle, f.Statut, f.EmiseLe, f.PayeeLe,
                f.MontantHtCentimes, f.TvaCentimes, f.MontantTtcCentimes,
                f.RaisonSociale, f.NumeroTva, f.ReferenceExterne,
                compte = u.Email,
                nom = (u.FirstName + " " + u.LastName).Trim(),
            })
            .ToListAsync(ct);

        var maintenant = DateTime.UtcNow;

        return Ok(lignes.Select(f => new
        {
            f.Id, f.Numero, f.Libelle, f.Statut, f.EmiseLe, f.PayeeLe,
            f.MontantHtCentimes, f.TvaCentimes, f.MontantTtcCentimes,
            f.RaisonSociale, f.NumeroTva, f.ReferenceExterne, f.compte, f.nom,
            impayee = f.Statut == "emise",
            // Trente jours : au-dela, ce n'est plus un retard de
            // traitement, c'est un dossier a reprendre.
            joursDepuisEmission = (int)(maintenant - f.EmiseLe).TotalDays,
        }));
    }

    /// <summary>
    /// La relance : un courriel, et rien d'autre.
    ///
    /// Aucun prelevement n'est declenche ici, et c'est une regle et non
    /// une limite technique. Le client recoit le numero, le montant, le
    /// motif du refus quand le prestataire l'a rendu, et un lien vers son
    /// espace. Le geste est trace : qui a relance, quand.
    /// </summary>
    [HttpPost("factures/{id:int}/relance")]
    public async Task<ActionResult<object>> Relancer(int id, CancellationToken ct)
    {
        var f = await _context.Factures.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f == null) return NotFound();

        if (f.Statut != "emise")
            return Conflict(new { message = "Cette facture n'est pas en attente de reglement." });

        var client = await _context.Users
            .Where(u => u.Id == f.UserId)
            .Select(u => new { u.Email, u.FirstName })
            .FirstOrDefaultAsync(ct);

        if (client?.Email is null)
            return Conflict(new { message = "Le compte rattache a cette facture n'a plus d'adresse." });

        await _mail.Envoyer(ModelesCourriel.RelanceFacture(
            client.Email, client.FirstName, f.Numero, f.MontantTtcCentimes, f.EmiseLe,
            f.ReferenceExterne, $"{Site}/facturation"), ct);

        await _log.Log("RelanceFacture", "Facture", id,
            $"Relance de la facture {f.Numero} envoyee a {client.Email}",
            UserId(), NomComplet(), Ip());

        return Ok(new { message = $"Relance envoyee a {client.Email}." });
    }

    /// <summary>
    /// Enregistrer un reglement recu autrement.
    ///
    /// Un virement arrive sur le compte bancaire sans passer par le
    /// prestataire : sans ce geste, la facture reste « emise » pour
    /// toujours et le client se fait relancer alors qu'il a paye. Ce
    /// n'est pas un encaissement — c'est la trace d'un encaissement qui a
    /// eu lieu ailleurs, et le journal dit qui l'a portee.
    /// </summary>
    [HttpPost("factures/{id:int}/payee")]
    public async Task<ActionResult<object>> MarquerPayee(int id, CancellationToken ct)
    {
        var f = await _context.Factures.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f == null) return NotFound();

        if (f.Statut == "payee")
            return Ok(new { message = "Cette facture etait deja reglee." });
        if (f.Statut == "annulee")
            return Conflict(new { message = "Une facture annulee ne se marque pas payee." });

        f.Statut = "payee";
        f.PayeeLe = DateTime.UtcNow;

        // L'abonnement suit : le laisser « impaye » couperait le service
        // d'un client qui vient de regler.
        var abo = await _context.Abonnements
            .Where(a => a.UserId == f.UserId && a.Statut == "impaye")
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);
        if (abo != null) abo.Statut = "actif";

        await _context.SaveChangesAsync(ct);

        await _log.Log("FactureMarqueePayee", "Facture", id,
            $"Facture {f.Numero} marquee reglee depuis la console"
            + (abo != null ? " ; abonnement remis en service" : ""),
            UserId(), NomComplet(), Ip());

        return Ok(new { message = $"Facture {f.Numero} enregistree comme reglee." });
    }

    // ══════════════════════════════════════
    //  Les mises en avant
    // ══════════════════════════════════════

    /// <summary>
    /// Celles qui tournent, et jusqu'a quand.
    ///
    /// C'est ce qu'un client demande au telephone, et la seule facon d'y
    /// repondre etait d'ouvrir la base.
    /// </summary>
    [HttpGet("mises-en-avant")]
    public async Task<ActionResult<object>> MisesEnAvant(CancellationToken ct)
    {
        var maintenant = DateTime.UtcNow;

        return Ok(await _context.MisesEnAvant
            .AsNoTracking()
            .Where(m => m.FinLe > maintenant.AddDays(-30))
            .OrderByDescending(m => m.FinLe)
            .Join(_context.JobOffers, m => m.JobOfferId, o => o.Id, (m, o) => new { m, o })
            .Join(_context.Users, x => x.m.UserId, u => u.Id, (x, u) => new
            {
                x.m.Id, x.m.JobOfferId, x.m.DebutLe, x.m.FinLe,
                x.m.MontantCentimes, x.m.Origine,
                offre = x.o.Title,
                entreprise = x.o.Company,
                compte = u.Email,
                encours = x.m.FinLe > maintenant,
            })
            .ToListAsync(ct));
    }
}
