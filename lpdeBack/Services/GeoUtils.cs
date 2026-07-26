using System.Globalization;
using System.Text;

namespace lpdeBack.Services;

/// <summary>
/// Geocodage leger et auto-suffisant (villes francaises principales) + calcul de distance.
/// Aucune dependance reseau : suffisant pour la recherche par rayon sur un job board FR.
/// </summary>
public static class GeoUtils
{
    // Villes triees par longueur de nom decroissante (les noms composes matchent avant les simples).
    private static readonly (string Name, double Lat, double Lng)[] Cities = new[]
    {
        ("Boulogne-Billancourt", 48.8358, 2.2400),
        ("Saint-Denis", 48.9362, 2.3574),
        ("Aix-en-Provence", 43.5297, 5.4474),
        ("Clermont-Ferrand", 45.7772, 3.0870),
        ("Noisy-le-Grand", 48.8486, 2.5528),
        ("Fontenay-sous-Bois", 48.8517, 2.4772),
        ("Saint-Etienne", 45.4397, 4.3872),
        ("Villeurbanne", 45.7719, 4.8902),
        ("Montreuil", 48.8638, 2.4485),
        ("Argenteuil", 48.9472, 2.2467),
        ("Courbevoie", 48.8973, 2.2560),
        ("Nanterre", 48.8924, 2.2065),
        ("La Defense", 48.8918, 2.2412),
        ("Le Havre", 49.4944, 0.1079),
        ("La Rochelle", 46.1603, -1.1511),
        ("Villejuif", 48.7938, 2.3592),
        ("Montrouge", 48.8156, 2.3138),
        ("Nice", 43.7102, 7.2620),
        ("Lyon", 45.7640, 4.8357),
        ("Marseille", 43.2965, 5.3698),
        ("Toulouse", 43.6047, 1.4442),
        ("Nantes", 47.2184, -1.5536),
        ("Strasbourg", 48.5734, 7.7521),
        ("Montpellier", 43.6108, 3.8767),
        ("Bordeaux", 44.8378, -0.5792),
        ("Lille", 50.6292, 3.0573),
        ("Rennes", 48.1173, -1.6778),
        ("Reims", 49.2583, 4.0317),
        ("Le Mans", 48.0061, 0.1996),
        ("Angers", 47.4784, -0.5632),
        ("Grenoble", 45.1885, 5.7245),
        ("Dijon", 47.3220, 5.0415),
        ("Nimes", 43.8367, 4.3601),
        ("Tours", 47.3941, 0.6848),
        ("Amiens", 49.8941, 2.2958),
        ("Metz", 49.1193, 6.1757),
        ("Besancon", 47.2378, 6.0241),
        ("Orleans", 47.9029, 1.9093),
        ("Rouen", 49.4432, 1.0993),
        ("Mulhouse", 47.7508, 7.3359),
        ("Caen", 49.1829, -0.3707),
        ("Nancy", 48.6921, 6.1844),
        ("Avignon", 43.9493, 4.8055),
        ("Poitiers", 46.5802, 0.3404),
        ("Versailles", 48.8014, 2.1301),
        ("Pau", 43.2951, -0.3708),
        ("Antibes", 43.5808, 7.1251),
        ("Cannes", 43.5528, 7.0174),
        ("Perpignan", 42.6887, 2.8948),
        ("Limoges", 45.8336, 1.2611),
        ("Brest", 48.3904, -4.4861),
        ("Toulon", 43.1242, 5.9280),
        ("Angouleme", 45.6484, 0.1562),
        ("Chambery", 45.5646, 5.9178),
        ("Annecy", 45.8992, 6.1294),
        ("Bourges", 47.0810, 2.3988),
        ("Colmar", 48.0794, 7.3585),
        ("Valence", 44.9334, 4.8924),
        ("Troyes", 48.2973, 4.0744),
        ("Lorient", 47.7477, -3.3660),
        ("Vannes", 47.6582, -2.7608),
        ("Quimper", 47.9960, -4.0973),
        ("Cergy", 49.0362, 2.0631),
        ("Creteil", 48.7904, 2.4556),
        ("Paris", 48.8566, 2.3522),
    };

    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var formD = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Retourne les coordonnees d'un lieu (ville FR reconnue) ou null.</summary>
    public static (double Lat, double Lng)? Geocode(string? location)
    {
        var norm = Normalize(location);
        if (norm.Length == 0) return null;
        foreach (var c in Cities)
        {
            if (norm.Contains(Normalize(c.Name)))
                return (c.Lat, c.Lng);
        }
        return null;
    }

    /// <summary>Distance en kilometres entre deux points (formule de haversine).</summary>
    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371.0;
        double dLat = ToRad(lat2 - lat1);
        double dLng = ToRad(lng2 - lng1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
