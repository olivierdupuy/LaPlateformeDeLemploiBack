using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace lpdeBack.Services;

/// <summary>Ce que le prestataire nous dit quand un paiement aboutit.</summary>
public record EvenementPaiement(
    string UserId,
    string Motif,
    int MontantCentimes,
    string Reference,
    bool Paye);

/// <summary>
/// L'encaissement.
///
/// Volontairement mince, et derriere une interface : la comptabilite du
/// site ne doit pas dependre du prestataire du jour. Changer de
/// prestataire doit se jouer ici et nulle part ailleurs — pas dans les
/// controleurs, pas dans les factures, pas dans les quotas.
///
/// Sans cle configuree, le service repond qu'il n'est pas disponible et
/// les appelants le disent franchement au recruteur. C'est le meme
/// parti que pour Brevo, OVH et le modele de langage : un site d'emploi
/// dont le paiement n'est pas branche reste un site d'emploi. Il perd
/// une recette, pas sa raison d'etre.
///
/// ── Ce qu'il reste a faire pour l'ouvrir ──
/// Renseigner « Paiement:CleSecrete » et « Paiement:SecretRetour »
/// (secrets de deploiement, jamais dans le depot), puis remplacer
/// l'appel HTTP de <see cref="CreerTunnel"/> par celui du prestataire
/// retenu. La signature de retour est deja verifiee ici.
/// </summary>
public class PrestatairePaiement
{
    private readonly IConfiguration _config;
    private readonly ILogger<PrestatairePaiement> _journal;
    private readonly IHttpClientFactory _clients;

    public PrestatairePaiement(IConfiguration config, ILogger<PrestatairePaiement> journal,
                               IHttpClientFactory clients)
    {
        _config = config;
        _journal = journal;
        _clients = clients;
    }

    private string? CleSecrete => _config["Paiement:CleSecrete"];
    private string? SecretRetour => _config["Paiement:SecretRetour"];
    private string UrlRetour => _config["App:PublicUrl"]?.TrimEnd('/') + "/recruteur/facturation";

    /// <summary>
    /// Vrai quand un prestataire est branche. Les appelants s'en
    /// servent pour ne pas afficher un bouton qui ne ferait rien.
    /// </summary>
    public bool EstConfigure => !string.IsNullOrWhiteSpace(CleSecrete)
                                && !string.IsNullOrWhiteSpace(SecretRetour);

    /// <summary>De quoi rendre compte a l'administration, sans livrer la cle.</summary>
    public string Etat => EstConfigure
        ? "Prestataire configure"
        : "Aucune cle de paiement : les achats sont refuses avec un message explicite";

    /// <summary>
    /// Ouvre un tunnel de paiement et rend l'adresse ou envoyer le
    /// navigateur.
    ///
    /// Le motif nous appartient : il dit ce qui est achete et revient
    /// tel quel dans le retour, ce qui evite de tenir une table
    /// d'intentions de paiement en plus.
    /// </summary>
    public Task<string> CreerTunnel(string userId, string libelle, int montantCentimes, string motif)
    {
        if (!EstConfigure)
            throw new InvalidOperationException("Aucun prestataire de paiement n'est configure.");

        // Le tunnel du prestataire s'ouvre ici. Tant qu'aucun n'est
        // retenu, on renvoie vers la page de facturation avec le motif :
        // le parcours est complet et verifiable de bout en bout, seule
        // l'etape d'encaissement manque.
        var adresse = $"{UrlRetour}?motif={Uri.EscapeDataString(motif)}"
                    + $"&montant={montantCentimes}"
                    + $"&libelle={Uri.EscapeDataString(libelle)}";

        _journal.LogInformation(
            "Tunnel de paiement demande : {Libelle} — {Montant} centimes", libelle, montantCentimes);

        return Task.FromResult(adresse);
    }

    /// <summary>
    /// Lit et verifie un retour de paiement.
    ///
    /// Rend null quand la signature ne correspond pas — et c'est le
    /// point : sans cette verification, n'importe qui pourrait
    /// s'attribuer une formule Pro en appelant l'adresse de retour avec
    /// un corps de son choix.
    /// </summary>
    public EvenementPaiement? LireRetour(string corps, string? signature)
    {
        if (!EstConfigure) return null;
        if (string.IsNullOrWhiteSpace(signature)) return null;
        if (!SignatureValide(corps, signature)) return null;

        try
        {
            using var document = JsonDocument.Parse(corps);
            var racine = document.RootElement;

            return new EvenementPaiement(
                UserId: Lire(racine, "userId") ?? string.Empty,
                Motif: Lire(racine, "motif") ?? string.Empty,
                MontantCentimes: int.TryParse(Lire(racine, "montantCentimes"), NumberStyles.Integer,
                                              CultureInfo.InvariantCulture, out var m) ? m : 0,
                Reference: Lire(racine, "reference") ?? string.Empty,
                Paye: Lire(racine, "statut") is "paye" or "paid" or "succeeded");
        }
        catch (JsonException ex)
        {
            _journal.LogWarning(ex, "Retour de paiement illisible");
            return null;
        }
    }

    private static string? Lire(JsonElement racine, string nom) =>
        racine.TryGetProperty(nom, out var valeur)
            ? valeur.ValueKind == JsonValueKind.Number ? valeur.ToString() : valeur.GetString()
            : null;

    /// <summary>
    /// HMAC-SHA256 du corps brut, compare en temps constant : une
    /// comparaison ordinaire fuit la signature attendue, octet par
    /// octet, a qui mesure le temps de reponse.
    /// </summary>
    private bool SignatureValide(string corps, string signature)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecretRetour!));
        var attendue = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(corps)))
                              .ToLowerInvariant();

        // Certains prestataires prefixent (« v1=… ») : on ne compare que
        // la partie hexadecimale.
        var recue = signature.Contains('=') ? signature.Split('=').Last().Trim() : signature.Trim();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(attendue),
            Encoding.UTF8.GetBytes(recue.ToLowerInvariant()));
    }
}
