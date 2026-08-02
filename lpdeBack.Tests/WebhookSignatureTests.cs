using System.Security.Cryptography;
using System.Text;
using lpdeBack.Services;

namespace lpdeBack.Tests;

/// <summary>
/// La signature des livraisons de webhook.
///
/// C'est elle, et rien d'autre, qui empeche un tiers connaissant l'URL
/// d'un partenaire de lui fabriquer de fausses notifications :
/// « ce candidat a ete embauche », « cette offre est fermee ». Le
/// partenaire agirait dessus.
///
/// Ces tests jouent le role du destinataire : ils recalculent la
/// signature comme le ferait le code d'en face, et verifient qu'elle ne
/// tient que pour le bon secret, le bon corps et le bon horodatage.
/// </summary>
public class WebhookSignatureTests
{
    private const string Secret = "whsec_0123456789abcdef";
    private const string Corps = """{"evenement":"candidature.creee","donnees":{"candidatureId":42}}""";
    private const string Horodatage = "1785000000";

    /// <summary>Ce que ferait le destinataire, ecrit independamment du code teste.</summary>
    private static string RecalculerCommeLeDestinataire(string secret, string horodatage, string corps)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var empreinte = hmac.ComputeHash(Encoding.UTF8.GetBytes(horodatage + "." + corps));
        return Convert.ToHexString(empreinte).ToLowerInvariant();
    }

    private static string PartieV1(string signature) =>
        signature.Split(',').First(p => p.StartsWith("v1="))[3..];

    [Fact]
    public void Le_destinataire_retrouve_la_signature()
    {
        var signature = WebhookService.Signer(Secret, Horodatage, Corps);

        Assert.Equal(
            RecalculerCommeLeDestinataire(Secret, Horodatage, Corps),
            PartieV1(signature));
    }

    [Fact]
    public void La_signature_porte_l_horodatage_en_clair()
    {
        // Sans lui, le destinataire ne peut pas refuser une livraison
        // trop ancienne, et une livraison capturee se rejoue des mois
        // plus tard telle quelle.
        var signature = WebhookService.Signer(Secret, Horodatage, Corps);
        Assert.StartsWith($"t={Horodatage},", signature);
    }

    [Fact]
    public void Un_corps_modifie_invalide_la_signature()
    {
        var signature = WebhookService.Signer(Secret, Horodatage, Corps);
        var falsifie = Corps.Replace("42", "43");

        Assert.NotEqual(
            RecalculerCommeLeDestinataire(Secret, Horodatage, falsifie),
            PartieV1(signature));
    }

    [Fact]
    public void Un_horodatage_modifie_invalide_la_signature()
    {
        // C'est ce qui rend le rejeu inoperant : changer la date pour
        // faire passer une vieille livraison casse la signature.
        var signature = WebhookService.Signer(Secret, Horodatage, Corps);

        Assert.NotEqual(
            RecalculerCommeLeDestinataire(Secret, "1786000000", Corps),
            PartieV1(signature));
    }

    [Fact]
    public void Un_autre_secret_produit_une_autre_signature()
    {
        var attendue = WebhookService.Signer(Secret, Horodatage, Corps);
        var usurpee = WebhookService.Signer("whsec_autre", Horodatage, Corps);

        Assert.NotEqual(attendue, usurpee);
    }

    [Fact]
    public void Un_secret_neuf_est_reconnaissable_et_assez_long()
    {
        var secret = WebhookService.NouveauSecret();

        // Le prefixe rend le secret detectable par les outils qui
        // balaient les depots publics a la recherche de cles fuitees.
        Assert.StartsWith("whsec_", secret);
        // 24 octets en hexadecimal : 48 caracteres, plus le prefixe.
        Assert.Equal(6 + 48, secret.Length);
    }

    [Fact]
    public void Deux_secrets_tires_de_suite_different()
    {
        Assert.NotEqual(WebhookService.NouveauSecret(), WebhookService.NouveauSecret());
    }
}
