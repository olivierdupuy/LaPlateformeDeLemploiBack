using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace lpdeBack.Middleware;

/// <summary>
/// Ce qui reste quand tout le reste a echoue.
///
/// Une exception non prevue sortait en page d'erreur brute : le
/// visiteur voyait un mur, et la trace partait dans le journal du
/// serveur sans que rien ne la relie a sa requete.
///
/// Ici, deux choses distinctes :
///
///   Au visiteur, une reponse propre et sans detail. Un message
///   d'exception raconte les chemins du serveur, les noms des tables,
///   parfois une chaine de connexion — ce n'est pas une information a
///   rendre.
///
///   Au journal, tout : le type, le message, la pile, la route, la
///   methode, et un numero de reference. Ce numero est le seul lien
///   entre ce que le visiteur a vu et ce que le journal a garde.
/// </summary>
public sealed class FiletErreur : IExceptionHandler
{
    private readonly ILogger<FiletErreur> _journal;

    public FiletErreur(ILogger<FiletErreur> journal) => _journal = journal;

    public ValueTask<bool> TryHandleAsync(HttpContext contexte, Exception ex, CancellationToken arret)
    {
        // Le client a ferme l'onglet, ou la requete a ete annulee. Ce
        // n'est pas une panne, et l'inscrire comme telle noierait les
        // vraies dans le bruit.
        if (ex is OperationCanceledException && arret.IsCancellationRequested)
            return ValueTask.FromResult(false);

        // Court, lisible a voix haute au telephone, et suffisant pour
        // retrouver la ligne : c'est tout ce qu'on lui demande.
        var reference = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        _journal.LogError(ex,
            "Erreur non rattrapee {Reference} — {Methode} {Chemin}",
            reference, contexte.Request.Method, contexte.Request.Path);

        return Repondre(contexte, reference, arret);
    }

    private static async ValueTask<bool> Repondre(HttpContext contexte, string reference, CancellationToken arret)
    {
        if (contexte.Response.HasStarted) return false;

        contexte.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await contexte.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Quelque chose s'est mal passé de notre côté.",
            Detail = $"L'incident est enregistré sous la référence {reference}. "
                   + "Si le problème persiste, indiquez-nous cette référence.",
        }, arret);

        return true;
    }
}
