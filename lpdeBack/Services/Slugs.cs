using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace lpdeBack.Services;

/// <summary>
/// Fabrique et resout les fragments d'adresse lisibles.
///
/// Les pages de parcours existaient, mais derriere des parametres de
/// requete — « /offres?category=Informatique&location=Paris ». Trois
/// consequences, toutes couteuses :
///
///   Le robots.txt exclut lui-meme ces variantes de l'exploration, et a
///   raison : les combinaisons de filtres se comptent par milliers pour
///   un meme catalogue, et elles devoreraient le budget d'exploration.
///
///   Une adresse a parametres ne dit rien a personne. « emploi
///   developpeur web paris » est ce que les gens tapent, et une adresse
///   qui reprend ces mots se classe et se partage.
///
///   Aucune de ces pages n'avait de titre propre : elles heritaient
///   toutes du meme, ce qui les rend indistinguables pour un moteur.
///
/// Ce fichier ne fait que la transformation, dans les deux sens. La
/// resolution vers un libelle reel se fait en base : un fragment est
/// une cle de recherche, pas une verite.
/// </summary>
public static class Slugs
{
    /// <summary>
    /// « Développeur Web (H/F) » devient « developpeur-web ».
    ///
    /// Sans accents ni ponctuation : une adresse accentuee s'encode en
    /// pourcentages des qu'elle est copiee, et devient illisible dans
    /// le resultat de recherche qu'elle devait justement rendre clair.
    /// </summary>
    public static string Fabriquer(string? valeur)
    {
        if (string.IsNullOrWhiteSpace(valeur)) return string.Empty;

        var sansAccents = new string(valeur
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        var texte = sansAccents.ToLowerInvariant();
        texte = Regex.Replace(texte, @"\(?\s*[hf]\s*/\s*[hf]\s*\)?", " ");
        texte = Regex.Replace(texte, @"[^a-z0-9]+", "-");

        return texte.Trim('-');
    }

    /// <summary>
    /// Le chemin inverse, approximatif et assume : « developpeur-web »
    /// redevient « developpeur web », qu'on confronte ensuite a la base.
    /// On ne cherche pas a retrouver la casse ni les accents d'origine —
    /// c'est la base qui les porte.
    /// </summary>
    public static string EnMots(string? fragment) =>
        string.IsNullOrWhiteSpace(fragment)
            ? string.Empty
            : fragment.Replace('-', ' ').Trim();

    /// <summary>
    /// Deux libelles designent-ils la meme chose ?
    ///
    /// La comparaison passe par le fragment : « Paris 15e », « 75 -
    /// Paris » et « paris » se rejoignent, ce qui evite qu'une page
    /// d'atterrissage ne rate la moitie de ses offres pour une
    /// difference d'ecriture.
    /// </summary>
    public static bool Correspond(string? a, string? b) =>
        Fabriquer(a) == Fabriquer(b);
}
