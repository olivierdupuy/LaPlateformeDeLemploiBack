using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace lpdeBack.Validation;

/// <summary>
/// Un formulaire ouvert a tout le monde, et donc aux robots.
///
/// Les deux champs ne sont jamais remplis par une personne : l'un est
/// invisible, l'autre mesure le temps passe sur le formulaire. Ils ne
/// remplacent pas la limitation de debit — c'est elle qui arrete les
/// attaques en volume — mais ils ecartent gratuitement les robots
/// naifs, ceux qui moissonnent un formulaire et remplissent tout ce
/// qu'ils y trouvent.
///
/// Aucun service tiers, aucun cookie, rien qui parte ailleurs : le
/// bandeau du site promet qu'aucun traceur n'est depose, et un CAPTCHA
/// commercial aurait rendu cette phrase fausse.
/// </summary>
public interface IFormulairePublic
{
    /// <summary>
    /// Le champ-piege. Invisible et hors du parcours au clavier, une
    /// personne ne peut pas le remplir ; un robot qui remplit tout ce
    /// qu'il trouve le remplit.
    ///
    /// Son nom compte : « site web » est plausible, donc tentant. Un
    /// champ nomme « piege » serait ignore par n'importe quel robot un
    /// peu ecrit.
    /// </summary>
    string? SiteWeb { get; set; }

    /// <summary>
    /// Millisecondes ecoulees entre l'affichage du formulaire et son
    /// envoi, mesurees par le client.
    ///
    /// Falsifiable, evidemment — comme tout ce qui vient du client.
    /// C'est un filtre a robots, pas un controle de securite : lire un
    /// formulaire, le comprendre et le remplir prend plus d'une seconde
    /// et demie a n'importe qui.
    /// </summary>
    int? MsSaisie { get; set; }
}

public static class AntiRobot
{
    /// <summary>
    /// En deca, ce n'est pas quelqu'un qui a lu le formulaire.
    ///
    /// Une seconde, et non deux : le seuil doit ecarter les robots sans
    /// jamais ecarter une personne. Un formulaire a un seul champ —
    /// « mot de passe oublie » — se remplit vite avec un gestionnaire de
    /// mots de passe, et se faire refuser sa demande parce qu'on a ete
    /// rapide serait absurde. Les robots naifs postent en quelques
    /// dizaines de millisecondes : la marge reste d'un facteur vingt.
    /// </summary>
    public const int DelaiMinimal = 1_000;

    /// <summary>
    /// La cle de partition d'une limitation de debit.
    ///
    /// Elle ne peut pas etre celle de l'audit : « SessionService.Ip »
    /// prefere « X-Forwarded-For », un en-tete que le client ecrit
    /// lui-meme. Pour journaliser, c'est acceptable ; pour compter, ce
    /// serait offrir le contournement — il suffirait de tirer une
    /// adresse au hasard a chaque requete pour n'etre jamais limite.
    ///
    /// On prend donc l'adresse de la connexion, la seule que le client
    /// ne choisit pas. L'en-tete ne sert que si la connexion vient de la
    /// machine elle-meme : c'est le cas derriere un relais local, ou
    /// tout le monde apparaitrait sinon sous « ::1 ».
    /// </summary>
    public static string Client(HttpContext http)
    {
        var directe = http.Connection.RemoteIpAddress;

        if (directe != null && !IPAddress.IsLoopback(directe))
            return directe.ToString();

        var transmise = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(transmise))
            return transmise.Split(',')[0].Trim();

        return directe?.ToString() ?? "inconnu";
    }

    /// <summary>Le compte, quand la limite se compte par personne et non par adresse.</summary>
    public static string Compte(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Client(http);
}

/// <summary>
/// Applique les deux controles a tout formulaire qui les declare.
///
/// Un filtre plutot qu'un appel dans chaque controleur : celui qu'on
/// oublierait d'ecrire est precisement celui qui serait exploite, et
/// rien ne le signalerait.
/// </summary>
public sealed class AntiRobotFilter : IActionFilter
{
    private readonly ILogger<AntiRobotFilter> _log;

    public AntiRobotFilter(ILogger<AntiRobotFilter> log) => _log = log;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is not IFormulairePublic f) continue;

            var motif =
                !string.IsNullOrWhiteSpace(f.SiteWeb) ? "champ-piege rempli"
                : f.MsSaisie is { } ms && ms >= 0 && ms < AntiRobot.DelaiMinimal
                    ? $"formulaire envoye en {ms} ms"
                    : null;

            if (motif == null) continue;

            _log.LogWarning("Envoi automatise ecarte ({Motif}) depuis {Ip} sur {Chemin}",
                            motif, AntiRobot.Client(context.HttpContext),
                            context.HttpContext.Request.Path);

            // Le message reste vague a dessein : detailler lequel des deux
            // controles a mordu apprendrait a le contourner. Une personne
            // ne le verra de toute facon jamais.
            context.Result = new BadRequestObjectResult(new
            {
                message = "Votre envoi n'a pas pu être traité. Rechargez la page et réessayez.",
            });
            return;
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
