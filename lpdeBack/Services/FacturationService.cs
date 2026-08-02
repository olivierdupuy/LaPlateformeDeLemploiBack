using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Formules, quotas, mises en avant et factures.
///
/// La mise en avant d'une offre existait deja — bouton, etiquette,
/// remontee dans le tri — mais elle etait gratuite et sans limite.
/// Autrement dit le seul levier economique du site etait offert ; et
/// comme tout le monde pouvait s'en servir, il ne distinguait plus
/// rien : quand toutes les offres sont mises en avant, aucune ne l'est.
///
/// Ce service tient les regles. Le paiement lui-meme est ailleurs
/// (<see cref="PrestatairePaiement"/>) : la comptabilite ne doit pas
/// dependre du prestataire du jour.
/// </summary>
public class FacturationService
{
    private readonly AppDbContext _context;
    private readonly ILogger<FacturationService> _journal;

    /// <summary>Une mise en avant dure quinze jours. Au-dela, l'offre a fait son effet ou ne le fera pas.</summary>
    public const int JoursMiseEnAvant = 15;

    /// <summary>Prix unitaire d'une mise en avant hors formule, en centimes.</summary>
    public const int PrixMiseEnAvantCentimes = 2900;

    public FacturationService(AppDbContext context, ILogger<FacturationService> journal)
    {
        _context = context;
        _journal = journal;
    }

    /// <summary>
    /// La formule active d'un recruteur.
    ///
    /// Une formule echue retombe sur la gratuite sans rien supprimer :
    /// les offres deja publiees restent en ligne, seule la publication
    /// d'une nouvelle est refusee au-dela du quota. Depublier
    /// automatiquement les offres d'un client dont le prelevement a
    /// echoue serait le meilleur moyen de le perdre pour de bon.
    /// </summary>
    public async Task<Formules.Definition> FormuleDe(string userId)
    {
        var abonnement = await _context.Abonnements
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.Statut == "actif")
            .OrderByDescending(a => a.DebutLe)
            .FirstOrDefaultAsync();

        if (abonnement is null) return Formules.Gratuit;
        if (abonnement.FinLe is not null && abonnement.FinLe < DateTime.UtcNow) return Formules.Gratuit;

        return Formules.Par(abonnement.Formule);
    }

    /// <summary>
    /// Peut-il publier une offre de plus ?
    ///
    /// Les brouillons ne comptent pas : ils ne sont visibles de
    /// personne, et facturer un brouillon reviendrait a faire payer
    /// l'hesitation.
    /// </summary>
    public async Task<(bool Autorise, string? Motif, int Utilisees, int Quota)> PeutPublier(string userId)
    {
        var formule = await FormuleDe(userId);
        if (formule.OffresActives < 0) return (true, null, 0, -1);

        var utilisees = await _context.JobOffers
            .CountAsync(o => o.CreatedByUserId == userId && o.IsActive && !o.IsDraft);

        if (utilisees < formule.OffresActives)
            return (true, null, utilisees, formule.OffresActives);

        return (false,
            $"Votre formule {formule.Nom} autorise {formule.OffresActives} offres en ligne. "
            + "Fermez une offre ou changez de formule pour en publier une nouvelle.",
            utilisees, formule.OffresActives);
    }

    /// <summary>
    /// Combien de mises en avant restent incluses ce mois-ci.
    ///
    /// Le mois est calendaire et non glissant : c'est celui que le
    /// client a en tete quand il lit « 5 par mois », et un mois
    /// glissant produit des refus incomprehensibles le 3 du mois.
    /// </summary>
    public async Task<int> MisesEnAvantRestantes(string userId)
    {
        var formule = await FormuleDe(userId);
        if (formule.MisesEnAvantIncluses == 0) return 0;

        var debutDuMois = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var consommees = await _context.MisesEnAvant
            .CountAsync(m => m.UserId == userId && m.Origine == "incluse" && m.DebutLe >= debutDuMois);

        return Math.Max(0, formule.MisesEnAvantIncluses - consommees);
    }

    /// <summary>
    /// Met une offre en avant. Rend la mise en avant creee, ou null si
    /// le quota est epuise et qu'aucun paiement n'a ete fourni.
    /// </summary>
    public async Task<MiseEnAvant?> MettreEnAvant(string userId, int offreId, bool payee, string? reference)
    {
        var incluse = !payee && await MisesEnAvantRestantes(userId) > 0;
        if (!incluse && !payee) return null;

        var mise = new MiseEnAvant
        {
            JobOfferId = offreId,
            UserId = userId,
            DebutLe = DateTime.UtcNow,
            FinLe = DateTime.UtcNow.AddDays(JoursMiseEnAvant),
            MontantCentimes = incluse ? 0 : PrixMiseEnAvantCentimes,
            Origine = incluse ? "incluse" : "payee",
            ReferenceExterne = reference,
        };

        _context.MisesEnAvant.Add(mise);

        var offre = await _context.JobOffers.FindAsync(offreId);
        if (offre is not null) offre.IsFeatured = true;

        await _context.SaveChangesAsync();
        return mise;
    }

    /// <summary>
    /// Retire les mises en avant echues.
    ///
    /// Sans cela, une offre poussee une fois reste en tete pour
    /// toujours : le client paie quinze jours et obtient l'eternite, et
    /// le classement cesse de vouloir dire quoi que ce soit.
    /// </summary>
    public async Task<int> RetirerLesEchues()
    {
        var maintenant = DateTime.UtcNow;

        var echues = await _context.MisesEnAvant
            .Where(m => m.FinLe < maintenant)
            .Select(m => m.JobOfferId)
            .Distinct()
            .ToListAsync();

        if (echues.Count == 0) return 0;

        // Une offre peut avoir ete poussee deux fois : on ne la retire
        // que si aucune mise en avant n'est encore valable.
        var encoreValables = await _context.MisesEnAvant
            .Where(m => m.FinLe >= maintenant && echues.Contains(m.JobOfferId))
            .Select(m => m.JobOfferId)
            .Distinct()
            .ToListAsync();

        var aRetirer = echues.Except(encoreValables).ToList();
        if (aRetirer.Count == 0) return 0;

        var offres = await _context.JobOffers
            .Where(o => aRetirer.Contains(o.Id) && o.IsFeatured)
            .ToListAsync();

        foreach (var o in offres) o.IsFeatured = false;
        await _context.SaveChangesAsync();

        _journal.LogInformation("{Nombre} mises en avant echues retirees", offres.Count);
        return offres.Count;
    }

    /// <summary>
    /// Emet une facture.
    ///
    /// Le numero est sequentiel et sans trou : c'est une obligation
    /// comptable, et c'est pourquoi il est attribue a l'emission et
    /// jamais reutilise, meme si la facture est annulee ensuite. Une
    /// facture annulee reste, marquee comme telle.
    ///
    /// Les montants sont figes ici et non recalcules a l'affichage : une
    /// facture ne change pas quand le tarif change.
    /// </summary>
    public async Task<Facture> Emettre(string userId, string libelle, int montantHtCentimes,
                                       string? reference = null, int tauxTvaMillimes = 2000)
    {
        var tva = (int)Math.Round(montantHtCentimes * (tauxTvaMillimes / 10_000.0), MidpointRounding.AwayFromZero);

        var facture = new Facture
        {
            Numero = await ProchainNumero(),
            UserId = userId,
            Libelle = libelle,
            MontantHtCentimes = montantHtCentimes,
            TauxTvaMillimes = tauxTvaMillimes,
            TvaCentimes = tva,
            MontantTtcCentimes = montantHtCentimes + tva,
            ReferenceExterne = reference,
        };

        _context.Factures.Add(facture);
        await _context.SaveChangesAsync();

        return facture;
    }

    private async Task<string> ProchainNumero()
    {
        var annee = DateTime.UtcNow.Year;
        var prefixe = $"F-{annee}-";

        var dernier = await _context.Factures
            .Where(f => f.Numero.StartsWith(prefixe))
            .OrderByDescending(f => f.Id)
            .Select(f => f.Numero)
            .FirstOrDefaultAsync();

        var rang = 1;
        if (dernier is not null && int.TryParse(dernier[prefixe.Length..], out var precedent))
            rang = precedent + 1;

        return prefixe + rang.ToString("D6");
    }
}
