using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using lpdeBack.Models;

namespace lpdeBack.Services;

/// <summary>
/// Le second facteur par SMS.
///
/// Le code lui-meme n'est pas invente ici : Identity en fabrique un a
/// partir du tampon de securite du compte et du numero, valable quelques
/// minutes. Rien n'est donc stocke, et changer de numero invalide les
/// codes en cours par construction.
///
/// Ce qui est ici, c'est ce qu'Identity ne fait pas : empecher qu'on
/// vide le compte OVH. Chaque SMS coute un credit. Un formulaire laisse
/// en boucle, ou quelqu'un qui s'amuse a demander un code toutes les
/// secondes sur un numero qui n'est pas le sien, epuiserait le solde en
/// une nuit — et rendrait le second facteur inoperant pour tout le monde.
///
/// Deux garde-fous, l'un durable et l'autre non :
///   - un delai entre deux envois, garde en base, qui survit a un
///     redemarrage ;
///   - un plafond horaire garde en memoire, qui se remet a zero avec le
///     processus. Le premier suffit a rendre l'abus couteux ; le second
///     borne la casse d'une attaque soutenue.
/// </summary>
public class DeuxFacteursSms
{
    /// <summary>Entre deux envois : le temps qu'un SMS met a arriver, plus une marge.</summary>
    public static readonly TimeSpan Delai = TimeSpan.FromSeconds(60);

    /// <summary>Par compte et par heure. Au-dela, c'est un abus, pas un usage.</summary>
    private const int PlafondHoraire = 5;

    private readonly UserManager<AppUser> _users;
    private readonly OvhSmsService _sms;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DeuxFacteursSms> _log;

    public DeuxFacteursSms(UserManager<AppUser> users, OvhSmsService sms,
                           IMemoryCache cache, ILogger<DeuxFacteursSms> log)
    {
        _users = users;
        _sms = sms;
        _cache = cache;
        _log = log;
    }

    public bool Disponible => _sms.EstConfigure;
    public string Etat => _sms.Etat;

    /// <summary>Ce que l'appel a produit, et de quoi le dire a l'interesse.</summary>
    public record Resultat(bool Parti, string Message, int? SecondesAAttendre = null);

    /// <summary>
    /// Fabrique un code et l'expedie au numero donne — celui du compte,
    /// ou celui qu'on est en train de verifier.
    /// </summary>
    public async Task<Resultat> Envoyer(AppUser user, string? numero = null)
    {
        var destinataire = OvhSmsService.Normaliser(numero ?? user.PhoneNumber);
        if (destinataire == null)
            return new Resultat(false, "Aucun numéro de téléphone valide n'est enregistré sur ce compte.");

        if (!_sms.EstConfigure)
            return new Resultat(false, "L'envoi de SMS n'est pas configuré sur ce serveur.");

        // ── Délai entre deux envois ──
        if (user.LastSmsSentAt is { } dernier)
        {
            var ecoule = DateTime.UtcNow - dernier;
            if (ecoule < Delai)
            {
                var reste = (int)Math.Ceiling((Delai - ecoule).TotalSeconds);
                return new Resultat(false,
                    $"Un code vient d'être envoyé. Patientez {reste} seconde{(reste > 1 ? "s" : "")} avant d'en demander un autre.",
                    reste);
            }
        }

        // ── Plafond horaire ──
        var cle = $"sms:{user.Id}";
        var envoyes = _cache.GetOrCreate(cle, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return 0;
        });
        if (envoyes >= PlafondHoraire)
        {
            _log.LogWarning("Plafond horaire de SMS atteint pour {UserId}", user.Id);
            return new Resultat(false,
                "Trop de codes demandés pour ce compte. Réessayez dans une heure, ou utilisez un code de secours.");
        }

        // Le code depend du tampon de securite et du numero : il n'est
        // stocke nulle part, et il devient faux si l'un des deux change.
        var code = await _users.GenerateChangePhoneNumberTokenAsync(user, destinataire);

        // ── Pourquoi ce message est si court ──
        //
        // Un SMS porte 160 caracteres, et OVH ajoute une quinzaine de
        // caracteres de clause STOP tant qu'aucun expediteur valide n'est
        // declare. La premiere redaction en faisait 166 : avec la clause,
        // 181, donc DEUX credits a chaque connexion — le solde s'epuisait
        // deux fois plus vite pour rien.
        //
        // Deuxieme piege, plus brutal : un seul caractere hors du jeu
        // GSM 03.38 fait basculer le message en UCS-2, ou un SMS ne porte
        // plus que 70 caracteres. « à », « é », « è », « ç » y sont ; « ê »,
        // « â », « î », « ô », « û » n'y sont pas. Ne pas ecrire « meme »
        // avec un accent circonflexe ici n'est pas une negligence.
        var (parti, erreur) = await _sms.Envoyer(destinataire,
            $"{code} est votre code de connexion à La Plateforme de l'emploi. " +
            "Valable quelques minutes. Ne le communiquez à personne.");

        if (!parti)
            return new Resultat(false, erreur ?? "Le SMS n'est pas parti.");

        _cache.Set(cle, envoyes + 1, TimeSpan.FromHours(1));
        user.LastSmsSentAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);

        return new Resultat(true, $"Un code vient d'être envoyé au {OvhSmsService.Masquer(destinataire)}.");
    }

    /// <summary>Verifie un code recu par SMS.</summary>
    public async Task<bool> Verifier(AppUser user, string? code, string? numero = null)
    {
        var destinataire = OvhSmsService.Normaliser(numero ?? user.PhoneNumber);
        if (destinataire == null || string.IsNullOrWhiteSpace(code)) return false;
        return await _users.VerifyChangePhoneNumberTokenAsync(
            user, SessionService.CodeApplication(code), destinataire);
    }
}
