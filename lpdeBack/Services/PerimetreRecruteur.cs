using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Ce qu'un recruteur a le droit de gerer.
///
/// Une offre appartenait a la personne qui l'avait deposee, et a elle
/// seule. Deux consequences, l'une genante et l'autre grave :
///
///   — la console listait deja les offres des collegues (« scope=team »)
///     sans permettre d'en ouvrir une seule : on voyait que son collegue
///     avait douze offres, sans jamais pouvoir en toucher une ;
///   — un recruteur qui quitte l'entreprise emportait ses offres et ses
///     candidatures avec lui. Plus personne ne pouvait repondre aux
///     candidats, sinon un administrateur de la plateforme.
///
/// Le perimetre devient donc l'entreprise. L'auteur reste inscrit sur
/// l'offre — c'est une trace utile — mais l'autorite se partage entre
/// les comptes qui declarent la meme entreprise, exactement comme la
/// fiche entreprise.
///
/// ── Ce que cela vaut, et ce que cela ne vaut pas ──
///
/// L'appartenance repose sur un nom declare au compte. Elle empeche
/// d'agir sur une entreprise qu'on n'a pas revendiquee ; elle n'empeche
/// pas de revendiquer un nom qui n'est pas le sien. Une appartenance
/// reelle demanderait de verifier le domaine de l'adresse contre le site
/// de l'entreprise — c'est le cran suivant, pas celui-ci.
///
/// Un compte sans entreprise declaree ne partage rien : il ne gere que
/// ce qu'il a depose. Sans quoi tous les comptes sans entreprise
/// formeraient une equipe commune, ce qui serait exactement l'inverse
/// du but.
/// </summary>
public class PerimetreRecruteur
{
    private readonly AppDbContext _db;

    /// <summary>
    /// Memoise le temps d'une requete : une meme page appelle souvent
    /// plusieurs fois, et l'equipe ne change pas entre deux lignes.
    /// </summary>
    private readonly Dictionary<string, List<string>> _equipes = new();
    private readonly Dictionary<string, string?> _societes = new();
    private readonly Dictionary<string, bool> _proprietaires = new();

    public PerimetreRecruteur(AppDbContext db) => _db = db;

    /// <summary>L'entreprise declaree par ce compte, ou null.</summary>
    public async Task<string?> Societe(string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        if (_societes.TryGetValue(userId, out var connue)) return connue;

        var nom = await _db.Users.Where(u => u.Id == userId)
                                 .Select(u => u.Company)
                                 .FirstOrDefaultAsync(ct);
        nom = string.IsNullOrWhiteSpace(nom) ? null : nom.Trim();
        _societes[userId] = nom;
        return nom;
    }

    /// <summary>
    /// Les comptes qui partagent le perimetre de celui-ci, lui compris.
    ///
    /// Sert aux listes et aux statistiques, la ou l'on filtre en base
    /// plutot que de verifier ligne a ligne.
    /// </summary>
    public async Task<List<string>> Equipe(string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return new List<string>();
        if (_equipes.TryGetValue(userId, out var connue)) return connue;

        var societe = await Societe(userId, ct);
        List<string> ids;

        if (societe == null)
        {
            // Sans entreprise declaree, on est seul : c'est le comportement
            // d'avant, et c'est le bon pour ce cas.
            ids = new List<string> { userId };
        }
        else
        {
            ids = await _db.Users
                .Where(u => u.Company != null && u.Company.Trim().ToLower() == societe.ToLower()
                            && (u.Role == "Recruiter" || u.Role == "Admin"))
                .Select(u => u.Id)
                .ToListAsync(ct);

            // Le compte lui-meme en fait partie, meme si son role venait a
            // changer entre-temps.
            if (!ids.Contains(userId)) ids.Add(userId);
        }

        _equipes[userId] = ids;
        return ids;
    }

    /// <summary>
    /// Ce compte peut-il GERER ce qu'a depose cet auteur ?
    ///
    /// Gerer, c'est ecrire : modifier une offre, la suspendre, changer le
    /// statut d'une candidature. La LECTURE, elle, reste partagee par
    /// toute l'equipe — c'est « Equipe() » qui la porte, et elle n'a pas
    /// bouge. Un membre voit tout, et n'ecrit que sur ce qui est a lui.
    ///
    /// Avant, il suffisait de declarer la meme entreprise pour pouvoir
    /// modifier et supprimer les offres de tout le monde. Cela convient a
    /// deux associes, pas a une equipe de dix : un nouvel arrivant avait
    /// le catalogue entier a sa main des sa premiere connexion.
    ///
    /// L'administration passe partout ; ce n'est pas juge ici, mais par
    /// l'appelant, qui seul connait le role porte par le jeton.
    /// </summary>
    public async Task<bool> PeutGerer(string? moiId, string? auteurId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(moiId) || string.IsNullOrEmpty(auteurId)) return false;

        // Ce qui est a moi est a moi, quel que soit mon role.
        if (moiId == auteurId) return true;

        var mienne = await Societe(moiId, ct);
        if (mienne == null) return false;

        var sienne = await Societe(auteurId, ct);
        if (sienne == null || !string.Equals(mienne, sienne, StringComparison.OrdinalIgnoreCase))
            return false;

        // Meme maison, mais toucher au travail d'un autre demande d'en
        // etre proprietaire.
        return await EstProprietaire(moiId, ct);
    }

    /// <summary>
    /// Ce compte peut-il VOIR ce qu'a depose cet auteur ?
    ///
    /// La meme maison suffit — c'est l'ancienne regle, et elle ne change
    /// pas. La distinction introduite par les roles porte sur l'ecriture
    /// seule : un membre voit tout le travail de l'equipe, et n'ecrit que
    /// sur le sien.
    ///
    /// Sert aussi a ce qui est de la COLLABORATION et non de la gestion :
    /// laisser une note d'equipe sur une candidature, par exemple. Passer
    /// ces gestes par « PeutGerer » viderait de son sens la notion meme de
    /// note partagee — seul le proprietaire aurait pu en ecrire.
    /// </summary>
    public async Task<bool> PeutVoir(string? moiId, string? auteurId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(moiId) || string.IsNullOrEmpty(auteurId)) return false;
        if (moiId == auteurId) return true;

        var mienne = await Societe(moiId, ct);
        if (mienne == null) return false;

        var sienne = await Societe(auteurId, ct);
        return sienne != null && string.Equals(mienne, sienne, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ce compte est-il proprietaire de son equipe ?
    ///
    /// Memoise comme le reste : une meme requete pose la question a chaque
    /// ligne d'une liste.
    /// </summary>
    public async Task<bool> EstProprietaire(string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        if (_proprietaires.TryGetValue(userId, out var connu)) return connu;

        var role = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.RoleEquipe)
            .FirstOrDefaultAsync(ct);

        var oui = role == RolesEquipe.Proprietaire;
        _proprietaires[userId] = oui;
        return oui;
    }
}
