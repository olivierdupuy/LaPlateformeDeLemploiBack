using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using lpdeBack.Models;

namespace lpdeBack.Validation;

/// <summary>
/// Reserve un geste aux comptes dont l'adresse a ete confirmee.
///
/// « RequireConfirmedEmail » etait a false : on pouvait s'inscrire avec
/// l'adresse de quelqu'un d'autre, et un recruteur declarer une
/// entreprise a laquelle rien ne le rattachait. Le jeton de
/// confirmation partait pourtant deja — personne ne verifiait qu'on
/// l'avait ouvert.
///
/// Fermer la porte a l'inscription serait excessif : chercher un
/// emploi, enregistrer une offre, postuler doivent rester immediats.
/// Ce qui exige une adresse verifiee, c'est ce qui ENGAGE — publier une
/// offre que le public lira, ecrire a quelqu'un.
///
/// Un compte ouvert par Google ou LinkedIn arrive confirme : le
/// fournisseur a deja fait la verification.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AdresseConfirmeeAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext contexte)
    {
        var utilisateur = contexte.HttpContext.User;
        if (utilisateur?.Identity?.IsAuthenticated != true) return; // [Authorize] s'en charge

        // L'administration n'a pas a etre bloquee par cela : elle
        // intervient sur des comptes qui ne sont pas les siens.
        if (utilisateur.IsInRole("Admin")) return;

        var comptes = contexte.HttpContext.RequestServices.GetService<UserManager<AppUser>>();
        if (comptes == null) return;

        var id = utilisateur.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id)) return;

        var compte = await comptes.FindByIdAsync(id);
        if (compte == null || compte.EmailConfirmed) return;

        // 403 et non 401 : le compte est bien identifie, c'est le geste
        // qui lui est refuse. Un 401 ferait croire au client que la
        // session a expire, et le deconnecterait pour rien.
        contexte.Result = new ObjectResult(new
        {
            message = "Confirmez votre adresse électronique pour cela. "
                    + "Le lien vous a été envoyé à l'inscription — nous pouvons vous le renvoyer.",
            adresseNonConfirmee = true,
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }
}
