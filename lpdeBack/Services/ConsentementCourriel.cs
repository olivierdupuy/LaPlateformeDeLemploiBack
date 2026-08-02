using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Ce qu'on a le droit d'envoyer, et a qui.
///
/// Deux questions distinctes, longtemps confondues :
///
///   **Est-ce que la personne le veut ?** Il n'y avait qu'un
///   interrupteur, celui de la lettre d'information. Alertes d'offres,
///   accuses de candidature, messages de recruteurs partaient sans
///   qu'on puisse en retrancher une categorie. Qui recevait trop
///   d'alertes n'avait qu'un geste a sa disposition : le bouton
///   « indesirable », qui emporte avec lui tout le reste.
///
///   **Est-ce que l'adresse existe encore ?** On ecrivait a des boites
///   fermees depuis des mois. Chaque rejet abime la reputation du
///   domaine expediteur, et cette reputation abimee fait tomber en
///   indesirable les courriels qui, eux, etaient attendus — les mots de
///   passe oublies au premier chef. Un site dont la reinitialisation
///   n'arrive plus est un site ou l'on ne peut plus entrer.
///
/// Trois envois echappent aux deux controles et c'est deliberé :
/// reinitialisation de mot de passe, confirmation d'adresse, alerte de
/// connexion inhabituelle. Ils repondent a une action de la personne ou
/// protegent son compte. Les couper au motif qu'elle a coche « ne plus
/// rien recevoir » lui nuirait.
/// </summary>
public class ConsentementCourriel
{
    private readonly AppDbContext _context;
    private readonly ILogger<ConsentementCourriel> _journal;

    public ConsentementCourriel(AppDbContext context, ILogger<ConsentementCourriel> journal)
    {
        _context = context;
        _journal = journal;
    }

    /// <summary>Les categories coupables. Le nom sert de cle cote client.</summary>
    public static readonly Dictionary<string, string> Categories = new()
    {
        ["alertes"] = "Alertes d'offres correspondant a mes recherches",
        ["candidatures"] = "Suivi de mes candidatures",
        ["messages"] = "Messages recus dans la messagerie",
        ["entretiens"] = "Invitations et rappels d'entretien",
        ["lettre"] = "Lettre d'information",
        ["actualites"] = "Nouveautes du site et enquetes",
    };

    /// <summary>
    /// Ce qui part quoi qu'il arrive. Le nommer ici plutot que de le
    /// laisser implicite : la prochaine personne qui ajoutera une
    /// categorie saura ou est la frontiere.
    /// </summary>
    public static readonly string[] Incontournables =
        { "securite", "mot_de_passe", "confirmation_adresse" };

    /// <summary>
    /// La personne accepte-t-elle cette categorie ?
    ///
    /// Repond « oui » quand rien n'est enregistre : ne pas s'etre
    /// prononce n'est pas un refus, et l'inverse couperait les alertes
    /// de tous les comptes existants au premier deploiement.
    /// </summary>
    public async Task<bool> Autorise(string email, string categorie)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (Incontournables.Contains(categorie)) return true;

        email = email.Trim().ToLowerInvariant();

        // Une adresse bloquee ne recoit plus rien de coupable, quelles
        // que soient ses preferences : elle ne les a peut-etre jamais
        // vues, puisqu'elle ne recoit plus.
        var bloquee = await _context.RetoursCourriel
            .AnyAsync(r => r.Email == email && r.Bloque);
        if (bloquee) return false;

        var prefs = await _context.PreferencesCourriel
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Email == email);

        if (prefs is null) return true;
        if (prefs.ToutRefuse) return false;

        return categorie switch
        {
            "alertes" => prefs.AlertesOffres,
            "candidatures" => prefs.SuiviCandidatures,
            "messages" => prefs.Messages,
            "entretiens" => prefs.Entretiens,
            "lettre" => prefs.LettreInformation,
            "actualites" => prefs.Actualites,
            _ => true,
        };
    }

    /// <summary>
    /// Les preferences d'une adresse, creees au besoin. Le jeton se
    /// fabrique ici : il doit exister avant qu'on ne pose le lien de
    /// gestion au pied du premier courriel.
    /// </summary>
    public async Task<PreferencesCourriel> Obtenir(string email)
    {
        email = email.Trim().ToLowerInvariant();

        var prefs = await _context.PreferencesCourriel
            .FirstOrDefaultAsync(p => p.Email == email);

        if (prefs is not null) return prefs;

        prefs = new PreferencesCourriel { Email = email, Jeton = NouveauJeton() };
        _context.PreferencesCourriel.Add(prefs);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Deux courriels partant en meme temps pour la meme adresse
            // se heurtent sur l'index unique. Celui qui perd relit.
            _context.Entry(prefs).State = EntityState.Detached;
            prefs = await _context.PreferencesCourriel.FirstAsync(p => p.Email == email);
        }

        return prefs;
    }

    /// <summary>
    /// Enregistre un rejet. Un rejet dur ou une plainte bloque tout de
    /// suite ; trois rejets doux valent un dur — une boite pleine se
    /// vide, une boite pleine depuis trois envois ne se videra plus.
    /// </summary>
    public async Task NoterRetour(string email, string type, string? motif)
    {
        email = email.Trim().ToLowerInvariant();

        var retour = await _context.RetoursCourriel.FirstOrDefaultAsync(r => r.Email == email);
        if (retour is null)
        {
            retour = new RetourCourriel { Email = email, Type = type, Motif = motif };
            _context.RetoursCourriel.Add(retour);
        }
        else
        {
            retour.Occurrences++;
            retour.DernierLe = DateTime.UtcNow;
            retour.Motif = motif ?? retour.Motif;
            // Un rejet dur apres des rejets doux emporte la decision.
            if (type != "doux") retour.Type = type;
        }

        retour.Bloque = retour.Type != "doux" || retour.Occurrences >= 3;

        // Une plainte est plus qu'un rejet : la personne a dit que nos
        // messages etaient indesirables. On coupe tout, y compris ce
        // qu'elle avait accepte, sans attendre qu'elle le fasse.
        if (type == "plainte")
        {
            var prefs = await Obtenir(email);
            prefs.ToutRefuse = true;
            prefs.MisAJourLe = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        _journal.LogInformation(
            "Retour courriel {Type} pour une adresse — blocage : {Bloque}", type, retour.Bloque);
    }

    /// <summary>
    /// Rouvre une adresse qu'on avait cessé de servir.
    ///
    /// Le blocage se declenche sur un signal du prestataire, et ce
    /// signal se trompe : une panne passagere du serveur destinataire
    /// remonte parfois en rejet dur, un filtre trop zele en plainte.
    /// L'adresse est alors coupee de tout — y compris de la
    /// reinitialisation de mot de passe, qui est precisement ce qu'on
    /// utilise quand on n'arrive plus a entrer. Sans porte de sortie,
    /// le compte est perdu pour son titulaire.
    ///
    /// Le compteur repart de zero : sans cela, un seul rejet ulterieur
    /// rebloquerait aussitot l'adresse qu'on vient de rouvrir.
    /// </summary>
    public async Task<bool> Debloquer(string email, string? parQui)
    {
        email = email.Trim().ToLowerInvariant();

        var retour = await _context.RetoursCourriel.FirstOrDefaultAsync(r => r.Email == email);
        if (retour is null) return false;

        retour.Bloque = false;
        retour.Occurrences = 0;
        retour.Type = "doux";
        retour.Motif = $"Debloquee manuellement le {DateTime.UtcNow:yyyy-MM-dd}";

        // Une plainte avait coupe toutes les categories. Debloquer sans
        // les rendre laisserait une adresse « joignable » qui ne recoit
        // toujours rien : le geste n'aurait aucun effet visible.
        var prefs = await _context.PreferencesCourriel.FirstOrDefaultAsync(p => p.Email == email);
        if (prefs is not null && prefs.ToutRefuse)
        {
            prefs.ToutRefuse = false;
            prefs.MisAJourLe = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        _journal.LogInformation("Adresse debloquee manuellement par {Qui}", parQui ?? "inconnu");
        return true;
    }

    /// <summary>
    /// Assez long pour n'etre pas devinable : il ouvre les preferences
    /// d'une adresse sans mot de passe.
    /// </summary>
    public static string NouveauJeton() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
