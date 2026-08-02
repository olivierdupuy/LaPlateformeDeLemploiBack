using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace lpdeBack.Services;

/// <summary>
/// Repondre « rien n'a change » plutot que de tout renvoyer.
///
/// Le cache de sortie evite de **recalculer** une reponse ; il ne
/// dispense pas de la **transmettre**. Un plan de site de cinq
/// mega-octets repart donc en entier a chaque passage de robot, plusieurs
/// fois par jour, pour chacun d'eux — alors qu'il n'a pas bouge depuis
/// le dernier import.
///
/// L'etiquette d'entite regle cela : le client la renvoie, on la
/// compare, et on repond 304 sans corps. Le cout est celui du calcul
/// d'une empreinte sur un texte deja produit ; le gain se compte en
/// giga-octets sur les gros fichiers.
///
/// Reservee aux reponses volumineuses et publiques. Sur une reponse de
/// deux kilo-octets, l'empreinte coute plus que ce qu'elle economise.
/// </summary>
public static class Etiquettes
{
    /// <summary>
    /// Rend 304 si le client a deja cette version, sinon rend le contenu
    /// en posant l'etiquette.
    ///
    /// L'etiquette est faible (« W/ ») : elle promet que la
    /// representation est equivalente, pas que les octets sont
    /// identiques. C'est exact — un plan de site regenere peut differer
    /// d'un espace sans rien changer de son sens.
    /// </summary>
    public static IActionResult AvecEtiquette(
        this ControllerBase controleur, string contenu, string typeMime)
    {
        var etiquette = Calculer(contenu);

        var connue = controleur.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(connue) && connue.Contains(etiquette))
        {
            // 304 : pas de corps, et c'est tout l'interet. Les en-tetes
            // de cache doivent y figurer quand meme, sinon le client
            // perd la duree de validite en meme temps que le contenu.
            controleur.Response.Headers.ETag = etiquette;
            return new StatusCodeResult(StatusCodes.Status304NotModified);
        }

        controleur.Response.Headers.ETag = etiquette;
        return controleur.Content(contenu, typeMime);
    }

    /// <summary>
    /// SHA-256 tronque a seize caracteres. La collision est ici sans
    /// gravite — au pire un client garde une version d'une heure de
    /// trop — et seize caracteres suffisent a ce qu'elle n'arrive
    /// jamais en pratique.
    /// </summary>
    private static string Calculer(string contenu)
    {
        var empreinte = SHA256.HashData(Encoding.UTF8.GetBytes(contenu));
        return $"W/\"{Convert.ToHexString(empreinte)[..16].ToLowerInvariant()}\"";
    }
}
