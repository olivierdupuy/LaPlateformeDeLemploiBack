using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Controllers;

/// <summary>
/// Signalement de contenu illicite (reglement europeen sur les services
/// numeriques, article 16).
///
/// Les mentions legales renvoyaient vers une adresse de courriel. Le
/// texte demande autre chose : un mecanisme electronique facile d'acces,
/// un accuse de reception, une decision motivee, et l'indication des
/// voies de recours. Une boite aux lettres n'en fournit aucun, et rien
/// ne prouvait qu'un signalement avait ete recu.
///
/// Ouvert sans compte — c'est la condition pour que le mecanisme
/// compte. En echange, le declarant recoit une reference qui lui permet
/// de suivre son dossier sans s'identifier.
/// </summary>
[ApiController]
[Route("api/signalements")]
public class DsaController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IEmailSender _courriel;
    private readonly ActivityLogService _activite;

    public DsaController(AppDbContext context, IEmailSender courriel, ActivityLogService activite)
    {
        _context = context;
        _courriel = courriel;
        _activite = activite;
    }

    /// <summary>
    /// Les motifs proposes. Ils viennent du texte : le declarant doit
    /// pouvoir dire en quoi le contenu est illicite, pas seulement qu'il
    /// lui deplait. Une liste libre produirait « nul » et « arnaque »,
    /// qui n'aident personne a instruire.
    /// </summary>
    private static readonly Dictionary<string, string> Motifs = new()
    {
        ["fausse_offre"] = "Offre fictive ou trompeuse",
        ["discrimination"] = "Discrimination a l'embauche",
        ["arnaque"] = "Escroquerie ou demande d'argent",
        ["donnees"] = "Collecte abusive de donnees personnelles",
        ["contrefacon"] = "Atteinte a un droit de propriete intellectuelle",
        ["haine"] = "Propos haineux ou harcelement",
        ["autre"] = "Autre contenu illicite",
    };

    public class SignalementEntrant : Validation.IFormulairePublic
    {
        [Required(ErrorMessage = "Indiquez ce que vous signalez.")]
        public string TypeContenu { get; set; } = "offre";

        public string? ContenuId { get; set; }
        public string? Url { get; set; }

        [Required(ErrorMessage = "Choisissez un motif.")]
        public string Motif { get; set; } = string.Empty;

        [Required(ErrorMessage = "Expliquez en quoi ce contenu est illicite.")]
        [MinLength(30, ErrorMessage = "Detaillez un peu : trente caracteres au minimum, pour qu'on puisse instruire.")]
        [MaxLength(5000)]
        public string Explication { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Cette adresse ne semble pas valide.")]
        public string? EmailDeclarant { get; set; }

        public bool DeclareBonneFoi { get; set; }

        public string? SiteWeb { get; set; }
        public int? MsSaisie { get; set; }
    }

    [HttpGet("motifs")]
    [AllowAnonymous]
    public IActionResult ListerMotifs() =>
        Ok(Motifs.Select(m => new { cle = m.Key, libelle = m.Value }));

    /// <summary>Depot d'un signalement.</summary>
    // Le filtre anti-robot est global : implementer IFormulairePublic
    // suffit a l'activer sur ce formulaire.
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("publication")]
    public async Task<IActionResult> Deposer([FromBody] SignalementEntrant entrant)
    {
        if (!Motifs.ContainsKey(entrant.Motif))
            return BadRequest(new { message = "Ce motif n'existe pas." });

        // La declaration de bonne foi n'est pas une case decorative : le
        // reglement lui donne un effet, elle engage le declarant sur
        // l'exactitude de ce qu'il affirme. Sans elle, le signalement ne
        // beneficie pas du traitement prioritaire prevu par le texte.
        if (!entrant.DeclareBonneFoi)
            return BadRequest(new
            {
                message = "Confirmez que vos declarations sont exactes et faites de bonne foi.",
            });

        var signalement = new SignalementDsa
        {
            Reference = await FabriquerReference(),
            TypeContenu = entrant.TypeContenu,
            ContenuId = entrant.ContenuId,
            Url = entrant.Url,
            Motif = entrant.Motif,
            Explication = entrant.Explication.Trim(),
            EmailDeclarant = entrant.EmailDeclarant?.Trim().ToLowerInvariant(),
            DeclareBonneFoi = true,
        };

        _context.SignalementsDsa.Add(signalement);
        await _context.SaveChangesAsync();

        // L'accuse de reception est une obligation, pas une politesse :
        // le texte exige qu'il soit envoye « sans retard ».
        if (!string.IsNullOrWhiteSpace(signalement.EmailDeclarant))
        {
            await _courriel.Envoyer(ModelesCourriel.AccuseSignalement(
                signalement.EmailDeclarant!,
                signalement.Reference,
                Motifs[signalement.Motif],
                signalement.CreeLe));
        }

        await _activite.Log("signalement_dsa_recu", "SignalementDsa", signalement.Id,
            $"{signalement.Reference} — {signalement.TypeContenu} / {signalement.Motif}");

        return Ok(new
        {
            reference = signalement.Reference,
            message = string.IsNullOrWhiteSpace(signalement.EmailDeclarant)
                ? "Signalement enregistre. Sans adresse de courriel, nous ne pourrons pas vous transmettre la decision : conservez cette reference."
                : "Signalement enregistre. Un accuse de reception vient de vous etre envoye.",
        });
    }

    /// <summary>
    /// Suivi par reference, sans compte. On ne rend que l'etat : le
    /// contenu du dossier appartient a qui l'a depose, et la reference
    /// seule ne prouve pas qu'on est cette personne.
    /// </summary>
    [HttpGet("{reference}")]
    [AllowAnonymous]
    public async Task<IActionResult> Suivre(string reference)
    {
        var s = await _context.SignalementsDsa
            .FirstOrDefaultAsync(x => x.Reference == reference);

        if (s is null) return NotFound(new { message = "Aucun signalement ne porte cette reference." });

        return Ok(new
        {
            s.Reference,
            s.Statut,
            s.CreeLe,
            s.TraiteLe,
            motif = Motifs.TryGetValue(s.Motif, out var m) ? m : s.Motif,
            s.Decision,
            s.MesurePrise,
        });
    }

    // ── Administration ──

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Lister([FromQuery] string? statut)
    {
        var q = _context.SignalementsDsa.AsQueryable();
        if (!string.IsNullOrWhiteSpace(statut)) q = q.Where(s => s.Statut == statut);

        var lignes = await q
            .OrderByDescending(s => s.CreeLe)
            .Take(300)
            .ToListAsync();

        return Ok(lignes);
    }

    public class DecisionEntrante
    {
        [Required] public string Statut { get; set; } = "Fonde";

        [Required(ErrorMessage = "La decision doit etre motivee : le reglement l'exige.")]
        [MinLength(20)]
        public string Decision { get; set; } = string.Empty;

        public string? MesurePrise { get; set; }
    }

    /// <summary>Instruire un signalement et notifier le declarant.</summary>
    [HttpPatch("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Decider(int id, [FromBody] DecisionEntrante d)
    {
        var s = await _context.SignalementsDsa.FindAsync(id);
        if (s is null) return NotFound();

        s.Statut = d.Statut;
        s.Decision = d.Decision.Trim();
        s.TraiteLe = DateTime.UtcNow;
        s.TraitePar = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // ── Executer la mesure, et ne declarer que ce qui a ete fait ──
        //
        // La mesure etait enregistree et jamais appliquee. Un moderateur
        // choisissait « contenu retire », le declarant recevait un
        // courriel le lui affirmant, et l'annonce restait en ligne. Le
        // texte impose une decision motivee : une decision qui ment sur
        // la mesure prise est pire qu'une absence de reponse, parce
        // qu'elle ferme le dossier.
        //
        // « Appliquer » rend ce qui a reellement ete fait. Si le contenu
        // a disparu entre-temps, ou si son type ne se traite pas
        // automatiquement, la mesure declaree redescend a « Aucune » et
        // le motif le dit — plutot que d'affirmer le contraire.
        s.MesurePrise = await Appliquer(s, d.MesurePrise);

        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(s.EmailDeclarant))
        {
            await _courriel.Envoyer(ModelesCourriel.DecisionSignalement(
                s.EmailDeclarant!, s.Reference, s.Statut == "Fonde", s.Decision!, s.MesurePrise));
        }

        await _activite.Log("signalement_dsa_traite", "SignalementDsa", s.Id,
            $"{s.Reference} — {s.Statut}");
        return NoContent();
    }

    /// <summary>
    /// Applique la mesure decidee, et rend celle qui a effectivement ete
    /// prise.
    ///
    /// Le retour n'est pas decoratif : c'est lui qui part dans le
    /// courriel de decision. Rendre autre chose que ce qui a ete fait
    /// reviendrait a signer une fausse declaration.
    ///
    /// Rien n'est supprime, tout est masque : une offre retiree garde
    /// ses candidatures, un avis masque reste consultable par
    /// l'administration. Un signalement peut etre juge fonde a tort, et
    /// une suppression ne se defait pas.
    /// </summary>
    private async Task<string> Appliquer(SignalementDsa s, string? mesure)
    {
        if (string.IsNullOrWhiteSpace(mesure) || mesure == "Aucune") return "Aucune";

        // Une mesure ne se prend que sur un signalement retenu. Retirer
        // un contenu jugé licite serait exactement ce que le reglement
        // cherche a eviter — le sur-retrait par prudence.
        if (s.Statut != "Fonde")
            return "Aucune (signalement non retenu)";

        if (!int.TryParse(s.ContenuId, out var cible))
            return "Aucune (contenu non identifie — a traiter a la main)";

        switch (mesure)
        {
            case "ContenuRetire" when s.TypeContenu == "offre":
            {
                var offre = await _context.JobOffers.FindAsync(cible);
                if (offre is null) return "Aucune (offre deja disparue)";
                EtatOffre.Appliquer(offre, false);
                offre.ModerationStatus = "Rejected";
                offre.ModerationNote = $"Retiree sur signalement {s.Reference} — {s.Motif}";
                return "ContenuRetire";
            }

            case "ContenuRetire" when s.TypeContenu == "avis":
            {
                var avis = await _context.CompanyReviews.FindAsync(cible);
                if (avis is null) return "Aucune (avis deja disparu)";
                avis.Status = "Rejected";
                return "ContenuRetire";
            }

            case "CompteSuspendu":
            {
                // Le compte est verrouille, pas efface : la suspension
                // se leve, la suppression non. Cent ans valent
                // « indefiniment » sans inventer un champ pour cela.
                var proprietaire = await ProprietaireDuContenu(s, cible);
                if (proprietaire is null) return "Aucune (auteur introuvable)";

                proprietaire.LockoutEnabled = true;
                proprietaire.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
                return "CompteSuspendu";
            }

            default:
                return $"Aucune ({mesure} non applicable a un contenu de type « {s.TypeContenu} »)";
        }
    }

    /// <summary>Qui a publie le contenu vise, quand on peut le savoir.</summary>
    private async Task<AppUser?> ProprietaireDuContenu(SignalementDsa s, int cible)
    {
        var id = s.TypeContenu switch
        {
            "offre" => (await _context.JobOffers.FindAsync(cible))?.CreatedByUserId,
            "avis" => (await _context.CompanyReviews.FindAsync(cible))?.AuthorUserId,
            _ => null,
        };

        return id is null ? null : await _context.Users.FindAsync(id);
    }

    /// <summary>
    /// « SIG-2026-0001 ». Lisible a voix haute au telephone, et le
    /// millesime evite qu'un compteur reparti a zero ne cree deux
    /// dossiers homonymes a deux ans d'ecart.
    /// </summary>
    private async Task<string> FabriquerReference()
    {
        var annee = DateTime.UtcNow.Year;
        var prefixe = $"SIG-{annee}-";

        var dernier = await _context.SignalementsDsa
            .Where(s => s.Reference.StartsWith(prefixe))
            .OrderByDescending(s => s.Id)
            .Select(s => s.Reference)
            .FirstOrDefaultAsync();

        var rang = 1;
        if (dernier is not null && int.TryParse(dernier[prefixe.Length..], out var precedent))
            rang = precedent + 1;

        return prefixe + rang.ToString("D4");
    }
}
