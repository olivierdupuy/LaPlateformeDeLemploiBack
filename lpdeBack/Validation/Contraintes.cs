using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace lpdeBack.Validation;

/// <summary>
/// Les contraintes qui reviennent partout.
///
/// Une longueur maximale n'est pas une coquetterie : sans elle, un champ
/// « Ville » accepte deux mégaoctets de texte, qui partent en base, dans
/// les listes d'administration, dans les courriels. Ce n'est pas une
/// faille au sens strict — c'est ce qui rend une table illisible et un
/// index inutilisable, et cela ne coûte rien à provoquer.
///
/// Les messages sont écrits en français et à la deuxième personne : ils
/// sont montrés tels quels, la fabrique de réponses les remonte au
/// client sans les traduire.
/// </summary>
public static class Limites
{
    public const int Nom = 100;
    public const int Adresse = 254;      // la borne d'une adresse de courriel (RFC 5321)
    public const int Ligne = 200;        // un intitulé, une ville, une société
    public const int Url = 500;
    public const int Paragraphe = 2_000;
    public const int Texte = 20_000;     // lettre de motivation, description d'offre
}

/// <summary>
/// Une longueur bornée, dite en français.
///
/// <c>[StringLength]</c> rend « The field Prenom must be a string with a
/// maximum length of 100. » — la phrase part telle quelle au client, qui
/// l'affiche à quelqu'un qui remplit un formulaire en français. Là où le
/// champ mérite mieux, on écrit un message explicite ; partout ailleurs,
/// celui-ci fait l'affaire.
/// </summary>
public sealed class LongueurAttribute : StringLengthAttribute
{
    public LongueurAttribute(int maximum) : base(maximum) { }

    public override string FormatErrorMessage(string name) =>
        MinimumLength > 0
            ? $"Ce champ doit faire entre {MinimumLength} et {MaximumLength} caractères."
            : $"Ce champ ne peut pas dépasser {MaximumLength} caractères.";
}

/// <summary>
/// Une adresse de courriel réellement postable.
///
/// <c>[EmailAddress]</c> se contente d'exiger une arobase entourée de
/// quelque chose : « a@b » passe, « <![CDATA[<img onerror=…>@x.fr]]> »
/// aussi. Or ces adresses s'affichent ensuite dans la console
/// d'administration. Le contrôle porte donc sur ce qu'une adresse peut
/// réellement contenir, et exige un domaine avec extension.
/// </summary>
public sealed class AdresseCourrielAttribute : ValidationAttribute
{
    private static readonly Regex Forme = new(
        @"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~.\-]+@[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}$",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    public override bool IsValid(object? value)
    {
        if (value is null) return true;                 // c'est [Required] qui exige la présence
        var s = value as string ?? "";
        if (string.IsNullOrWhiteSpace(s)) return true;
        s = s.Trim();
        return s.Length <= Limites.Adresse && !s.Contains("..") && Forme.IsMatch(s);
    }

    public override string FormatErrorMessage(string name) =>
        "Cette adresse e-mail ne semble pas valide.";
}

/// <summary>
/// Un numéro de mobile français, sous n'importe quelle écriture.
///
/// On le saisit avec des espaces, des points, un préfixe international :
/// refuser ces formes ferait corriger une saisie qui n'a rien de faux.
/// Seul ce qui reste après nettoyage est jugé.
/// </summary>
public sealed class TelephoneFrAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        var s = value as string ?? "";
        if (string.IsNullOrWhiteSpace(s)) return true;

        var chiffres = new string(s.Where(char.IsDigit).ToArray());
        if (s.TrimStart().StartsWith("+33")) chiffres = "0" + chiffres[2..];
        return chiffres.Length == 10 && chiffres[0] == '0' && chiffres[1] != '0';
    }

    public override string FormatErrorMessage(string name) =>
        "Ce numéro de téléphone ne semble pas valide. Exemple : 06 12 34 56 78.";
}

/// <summary>
/// Une adresse web, et seulement en http ou https.
///
/// Sans cette restriction, un champ « site de l'entreprise » accepte
/// « javascript:… » ou « data:… ». Le lien est ensuite rendu tel quel
/// dans une fiche publique : le clic exécuterait ce que l'auteur a
/// écrit, dans la session de qui l'a ouvert.
///
/// Un chemin interne est admis — « /uploads/resumes/… ». Ce n'est pas
/// une brèche : il ne peut désigner que ce serveur, et c'est sous cette
/// forme que le téléversement enregistre un CV, un avatar, un logo.
/// N'accepter que l'absolu aurait refusé toute candidature portant un
/// CV, ce qui est le cas ordinaire.
///
/// Le double « // » est écarté au passage : « //ailleurs.fr » est un
/// chemin pour qui lit naïvement, et une adresse absolue pour le
/// navigateur.
/// </summary>
public sealed class AdresseWebAttribute : ValidationAttribute
{
    /// <summary>Refuser un chemin interne, pour un champ qui vise l'extérieur.</summary>
    public bool ExterneSeulement { get; set; }

    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        var s = (value as string ?? "").Trim();
        if (s.Length == 0) return true;
        if (s.Length > Limites.Url) return false;

        if (!ExterneSeulement && s.StartsWith('/') && !s.StartsWith("//"))
            return true;

        return Uri.TryCreate(s, UriKind.Absolute, out var u)
            && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(u.Host)
            && u.Host.Contains('.');
    }

    public override string FormatErrorMessage(string name) =>
        "Indiquez une adresse web complète, commençant par http:// ou https://.";
}

/// <summary>
/// Une valeur prise dans une liste connue.
///
/// Un statut, un type de contrat, un rôle ne se saisissent pas : ils se
/// choisissent. Les laisser libres remplit la base de variantes qu'aucun
/// filtre ne retrouvera — « CDI », « cdi », « C.D.I. » — et permet
/// d'écrire un statut qui n'existe pas dans le code qui le lit.
/// </summary>
public sealed class ParmiAttribute : ValidationAttribute
{
    private readonly string[] _valeurs;
    private readonly bool _sensibleALaCasse;

    public ParmiAttribute(params string[] valeurs) : this(false, valeurs) { }

    public ParmiAttribute(bool sensibleALaCasse, params string[] valeurs)
    {
        _valeurs = valeurs;
        _sensibleALaCasse = sensibleALaCasse;
    }

    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        var s = (value as string ?? "").Trim();
        if (s.Length == 0) return true;
        var comparaison = _sensibleALaCasse
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return _valeurs.Any(v => string.Equals(v, s, comparaison));
    }

    public override string FormatErrorMessage(string name) =>
        $"Valeur inattendue. Choisissez parmi : {string.Join(", ", _valeurs)}.";
}

/// <summary>
/// Du texte, pas du balisage.
///
/// Ce contrôle ne remplace pas l'échappement à l'affichage — c'est lui
/// qui protège vraiment, et il reste en place. Il évite seulement qu'une
/// personne dépose sciemment des balises dans un champ qui n'en attend
/// pas : un nom, une ville, un intitulé de poste. Le refus est immédiat
/// et explicite, plutôt qu'un texte accepté puis affiché échappé, que
/// son auteur croirait avoir réussi à placer.
///
/// Les champs de texte long — lettre de motivation, description d'une
/// offre — en sont exemptés : on y écrit « <![CDATA[<3]]> » ou « a < b »
/// sans mauvaise intention.
/// </summary>
public sealed class SansBalisageAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        var s = value as string ?? "";
        return !s.Contains('<') && !s.Contains('>');
    }

    public override string FormatErrorMessage(string name) =>
        "Les caractères « < » et « > » ne sont pas acceptés ici.";
}
