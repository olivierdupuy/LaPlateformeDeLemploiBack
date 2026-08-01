using System.Collections.Concurrent;

namespace lpdeBack.Services;

/// <summary>
/// Ce que les taches de fond ont fait, et quand.
///
/// Les taches de fond sont les plus exposees de l'application : elles
/// n'ont pas de requete pour porter leur erreur, donc elles echouent en
/// silence. Une redaction de lettre qui leve une exception toutes les
/// six heures ne se voit nulle part — jusqu'au jour ou l'on s'etonne de
/// ne plus recevoir de brouillon.
///
/// Chacune depose ici son dernier passage. La sonde de sante les relit,
/// et signale celles qui ont pris du retard ou echoue.
/// </summary>
public static class EtatDesServices
{
    private sealed record Passage(DateTime Quand, bool Reussi, string Detail);

    /// <summary>L'etat d'une tache, tel que la sonde le publie.</summary>
    public sealed record Ligne(string Service, string Etat, DateTime? DernierPassage, string? Detail)
    {
        /// <summary>Vrai si cette tache demande de l'attention.</summary>
        public bool Inquiete => Etat is "en échec" or "en retard" or "jamais passé";
    }

    private static readonly ConcurrentDictionary<string, Passage> _passages = new();

    /// <summary>Au-dela de ce retard, une tache est consideree en peine.</summary>
    private static readonly Dictionary<string, TimeSpan> _cadences = new()
    {
        ["redaction-newsletter"] = TimeSpan.FromHours(18),
        ["purge"] = TimeSpan.FromHours(36),
    };

    public static void Noter(string service, bool reussi, string detail)
        => _passages[service] = new Passage(DateTime.UtcNow, reussi, detail);

    /// <summary>
    /// L'etat de chaque tache : « sain », « en retard », « en echec »,
    /// « en attente » ou « jamais passe ».
    ///
    /// « En attente » n'est pas une panne : une tache quotidienne n'a
    /// rien fait pendant la premiere minute, et cela ne justifie pas de
    /// reveiller quelqu'un. On ne s'inquiete qu'apres une cadence
    /// entiere depuis le demarrage.
    /// </summary>
    public static IReadOnlyList<Ligne> Rapport(TimeSpan depuisDemarrage)
    {
        var lignes = new List<Ligne>();
        foreach (var (nom, cadence) in _cadences)
        {
            _passages.TryGetValue(nom, out var p);

            string etat;
            if (p == null) etat = depuisDemarrage < cadence ? "en attente" : "jamais passé";
            else if (!p.Reussi) etat = "en échec";
            else if (DateTime.UtcNow - p.Quand > cadence) etat = "en retard";
            else etat = "sain";

            lignes.Add(new Ligne(nom, etat, p?.Quand, p?.Detail));
        }
        return lignes;
    }
}
