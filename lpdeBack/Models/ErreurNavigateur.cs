namespace lpdeBack.Models;

/// <summary>
/// Ce qui a casse chez le visiteur.
///
/// Une exception JavaScript en production n'allait nulle part : elle
/// s'inscrivait dans une console que personne n'ouvre, sur un appareil
/// qu'on ne possede pas. Un bouton mort pour tous les utilisateurs de
/// Safari pouvait tenir des semaines sans laisser de trace — le serveur,
/// lui, repondait 200 a tout.
///
/// Ce que cette table ne contient pas est aussi delibere que ce qu'elle
/// contient : ni identifiant de compte, ni adresse, ni contenu de
/// formulaire. Le chemin suffit a reproduire, et le reste serait un
/// fichier de donnees personnelles constitue par accident.
/// </summary>
public class ErreurNavigateur
{
    public int Id { get; set; }

    /// <summary>Le message de l'exception, tronque a la source.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// La pile, telle que le navigateur la donne. Minifiee — les noms
    /// d'origine ne se retrouvent qu'avec les cartes de source, que l'on
    /// ne publie pas. Elle sert surtout a distinguer deux fautes qui
    /// portent le meme message.
    /// </summary>
    public string? Pile { get; set; }

    /// <summary>Ou l'on etait. La partie la plus utile pour reproduire.</summary>
    public string? Chemin { get; set; }

    /// <summary>
    /// L'agent utilisateur. Il dit le navigateur et sa version, ce qui
    /// tranche la question « est-ce que ca ne casse que sur Safari ».
    /// </summary>
    public string? Navigateur { get; set; }

    /// <summary>
    /// Empreinte du message et de la tete de pile. Sert a regrouper :
    /// une meme panne vue mille fois est une ligne avec un compteur, pas
    /// mille lignes.
    /// </summary>
    public string Empreinte { get; set; } = string.Empty;

    /// <summary>Combien de fois cette meme faute a ete remontee.</summary>
    public int Occurrences { get; set; } = 1;

    public DateTime PremiereVue { get; set; } = DateTime.UtcNow;
    public DateTime DerniereVue { get; set; } = DateTime.UtcNow;

    /// <summary>Marquee comme traitee : elle sort de la liste sans etre effacee.</summary>
    public bool Traitee { get; set; }
}
