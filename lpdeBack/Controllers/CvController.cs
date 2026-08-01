using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using lpdeBack.Data;
using lpdeBack.Models;
using lpdeBack.DTOs;
using lpdeBack.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Candidate")]
public class CvController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly AiClient _ai;

    private static readonly string[] ValidTypes = { "Experience", "Formation", "Langue", "Competence", "CentreInteret", "Projet" };

    public CvController(AppDbContext context, AiClient ai)
    {
        _context = context;
        _ai = ai;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Ramene le libelle de categorie produit par le modele vers l'un des six
    /// types attendus. Un modele ouvert ecrit volontiers « Competences » ou
    /// « Expérience » : sans cette remise en forme, ces sections — pourtant
    /// correctement extraites — seraient silencieusement jetees.
    /// </summary>
    private static string? NormalizeSectionType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var key = new string(raw
            .Normalize(NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetter)
            .ToArray()).ToLowerInvariant();

        if (key.StartsWith("experience")) return "Experience";
        if (key.StartsWith("formation") || key.StartsWith("education") || key.StartsWith("diplome")) return "Formation";
        if (key.StartsWith("langue")) return "Langue";
        if (key.StartsWith("competence") || key.StartsWith("skill")) return "Competence";
        if (key.StartsWith("centreinteret") || key.StartsWith("interet") || key.StartsWith("loisir")
            || key.StartsWith("hobb")) return "CentreInteret";
        if (key.StartsWith("projet") || key.StartsWith("project")) return "Projet";
        return null;
    }

    /// <summary>Remet les categories en forme puis ecarte ce qui reste inclassable.</summary>
    private static List<CvSectionCreateDto> KeepUsableSections(IEnumerable<CvSectionCreateDto> sections)
    {
        var kept = new List<CvSectionCreateDto>();
        foreach (var section in sections)
        {
            var type = NormalizeSectionType(section.SectionType);
            if (type == null || string.IsNullOrWhiteSpace(section.Title)) continue;
            section.SectionType = type;
            kept.Add(section);
        }
        return kept;
    }

    /// <summary>Lit la reponse du modele, qu'elle soit un tableau ou un objet « sections ».</summary>
    private static List<CvSectionCreateDto>? ReadSections(string content)
    {
        try
        {
            if (content.TrimStart().StartsWith('['))
                return JsonSerializer.Deserialize<List<CvSectionCreateDto>>(content, FlexibleJson.Options);

            var wrapper = JsonSerializer.Deserialize<AiGenerateResponseDto>(content, FlexibleJson.Options);
            if (wrapper?.Sections is { Count: > 0 }) return wrapper.Sections;
        }
        catch (JsonException)
        {
            // On retente sur le premier tableau rencontre : certains modeles
            // ajoutent une phrase avant ou apres le JSON demande.
        }

        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        if (start < 0 || end <= start) return null;

        try
        {
            return JsonSerializer.Deserialize<List<CvSectionCreateDto>>(content[start..(end + 1)], FlexibleJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CvSection>>> GetAll()
    {
        return await _context.CvSections
            .Where(c => c.UserId == GetUserId())
            .OrderBy(c => c.SectionType)
            .ThenBy(c => c.SortOrder)
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<CvSection>> Create(CvSectionCreateDto dto)
    {
        var userExists = await _context.Users.AnyAsync(u => u.Id == GetUserId());
        if (!userExists) return Unauthorized(new { message = "Session expiree. Veuillez vous reconnecter." });

        if (!ValidTypes.Contains(dto.SectionType))
            return BadRequest("Type de section invalide.");

        var section = MapFromDto(dto);
        _context.CvSections.Add(section);
        await _context.SaveChangesAsync();
        return Ok(section);
    }

    [HttpPost("batch")]
    public async Task<ActionResult<IEnumerable<CvSection>>> CreateBatch(List<CvSectionCreateDto>? dtos)
    {
        if (dtos == null || dtos.Count == 0)
            return BadRequest(new { message = "Aucune section fournie." });

        try
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == GetUserId());
            if (!userExists) return Unauthorized(new { message = "Session expiree. Veuillez vous reconnecter." });

            var sections = new List<CvSection>();
            foreach (var dto in dtos)
            {
                if (!ValidTypes.Contains(dto.SectionType)) continue;
                sections.Add(MapFromDto(dto));
            }

            if (sections.Count == 0)
                return BadRequest(new { message = "Aucune section valide." });

            _context.CvSections.AddRange(sections);
            await _context.SaveChangesAsync();
            return Ok(sections);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Erreur lors de la sauvegarde.", detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, CvSectionUpdateDto dto)
    {
        var section = await _context.CvSections.FirstOrDefaultAsync(c => c.Id == id && c.UserId == GetUserId());
        if (section == null) return NotFound();

        section.Title = dto.Title;
        section.Organization = dto.Organization;
        section.Location = dto.Location;
        section.StartDate = dto.StartDate;
        section.EndDate = dto.EndDate;
        section.Description = dto.Description;
        section.Level = dto.Level;
        section.SortOrder = dto.SortOrder;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var section = await _context.CvSections.FirstOrDefaultAsync(c => c.Id == id && c.UserId == GetUserId());
        if (section == null) return NotFound();

        _context.CvSections.Remove(section);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("all")]
    public async Task<IActionResult> DeleteAll()
    {
        var sections = await _context.CvSections.Where(c => c.UserId == GetUserId()).ToListAsync();
        _context.CvSections.RemoveRange(sections);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ═══ Generation par le modele de langage ═══

    [HttpPost("generate-ai")]
    [EnableRateLimiting("ia")]
    public async Task<ActionResult<List<CvSectionCreateDto>>> GenerateWithAi(AiGenerateRequestDto? dto)
    {
        var user = await _context.Users.FindAsync(GetUserId());
        if (user == null) return Unauthorized();

        var prompt = BuildPrompt(user, dto?.AdditionalContext);

        var result = await _ai.ChatAsync(
            "Tu es un assistant RH expert en redaction de CV professionnels en francais. Reponds UNIQUEMENT avec un JSON valide.",
            prompt,
            temperature: 0.7,
            maxTokens: 3000,
            cancellationToken: HttpContext.RequestAborted);

        if (!result.Ok)
            return StatusCode(result.Status, new { message = result.Error });

        var sections = ReadSections(result.Content!);
        if (sections == null)
            return BadRequest(new { message = "Reponse du modele illisible. Reessayez." });

        sections = KeepUsableSections(sections);
        if (sections.Count == 0)
            return BadRequest(new { message = "Aucune section exploitable n'a ete generee. Reessayez." });

        return Ok(sections);
    }

    // ═══ Analyse d'un fichier CV (PDF/DOCX/DOC) par le modele ═══

    [HttpPost("parse-file")]
    public async Task<ActionResult<List<CvSectionCreateDto>>> ParseCvFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Aucun fichier envoye." });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "Le fichier ne doit pas depasser 10 Mo." });

        var extraction = ExtractText(file);
        if (extraction.Error != null)
            return BadRequest(new { message = extraction.Error });

        var prompt = BuildParsePrompt(extraction.Text!);

        var result = await _ai.ChatAsync(
            "Tu es un expert RH specialise dans l'analyse et la structuration de CV professionnels. Tu extrais TOUTES les informations d'un CV de maniere exhaustive et fidele. Tu reponds UNIQUEMENT en JSON valide, sans aucun texte autour.",
            prompt,
            temperature: 0.2,
            maxTokens: 6000,
            cancellationToken: HttpContext.RequestAborted);

        if (!result.Ok)
            return StatusCode(result.Status, new { message = result.Error });

        var sections = ReadSections(result.Content!);
        if (sections == null)
            return BadRequest(new { message = "Reponse du modele illisible. Reessayez." });

        sections = KeepUsableSections(sections);
        if (sections.Count == 0)
            return BadRequest(new { message = "Aucune section extraite du CV. Verifiez le contenu du fichier." });

        return Ok(new { sections, truncated = extraction.Truncated });
    }

    // ═══ Parse CV -> champs de profil (prefill) ═══

    public class ProfileDraftDto
    {
        public string? Title { get; set; }
        public string? Skills { get; set; }
        public int? ExperienceYears { get; set; }
        public string? Education { get; set; }
        public string? City { get; set; }
        public string? Bio { get; set; }
    }

    [HttpPost("parse-profile")]
    public async Task<ActionResult<ProfileDraftDto>> ParseProfile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Aucun fichier envoye." });
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "Le fichier ne doit pas depasser 10 Mo." });

        var extraction = ExtractText(file);
        if (extraction.Error != null)
            return BadRequest(new { message = extraction.Error });

        var prompt = "A partir du CV ci-dessous, renvoie UNIQUEMENT un objet JSON (sans texte autour) avec ces cles : " +
            "\"title\" (intitule de poste actuel ou recherche), \"skills\" (competences cles separees par des virgules), " +
            "\"experienceYears\" (nombre entier d'annees d'experience, ou null), \"education\" (diplome le plus eleve), " +
            "\"city\" (ville), \"bio\" (resume professionnel de 2-3 phrases a la premiere personne). " +
            "Chaque valeur est une CHAINE de caracteres (jamais une liste), sauf experienceYears qui est un entier. " +
            "Utilise null pour toute information absente.\n\nCV:\n" + extraction.Text;

        var result = await _ai.ChatAsync(
            "Tu es un expert RH. Tu extrais des informations de profil depuis un CV et reponds UNIQUEMENT en JSON valide.",
            prompt,
            temperature: 0.2,
            maxTokens: 800,
            cancellationToken: HttpContext.RequestAborted);

        if (!result.Ok)
            return StatusCode(result.Status, new { message = result.Error });

        var content = result.Content!;
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
            return BadRequest(new { message = "Reponse du modele illisible. Reessayez." });

        try
        {
            var draft = JsonSerializer.Deserialize<ProfileDraftDto>(content[start..(end + 1)], FlexibleJson.Options);
            return Ok(draft ?? new ProfileDraftDto());
        }
        catch (JsonException)
        {
            return BadRequest(new { message = "Reponse du modele illisible. Reessayez." });
        }
    }

    // ═══ Lecture du fichier ═══

    private record TextExtraction(string? Text, string? Error, bool Truncated);

    /// <summary>Longueur de texte transmise au modele. Au-dela, la fin du CV est
    /// ecartee : l'appelant en est informe plutot que de l'ignorer en silence.</summary>
    private const int MaxCvTextLength = 12000;

    private static TextExtraction ExtractText(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLower();

        string text;
        try
        {
            using var memoryStream = new MemoryStream();
            file.CopyTo(memoryStream);
            memoryStream.Position = 0;
            text = ext == ".pdf" ? ExtractTextFromPdf(memoryStream) : ExtractTextFromDocx(memoryStream);
        }
        catch (Exception)
        {
            // Cas courant : un « .doc » Word 97-2003, que le lecteur OpenXML ne
            // sait pas ouvrir. Le message doit orienter vers la sortie.
            var hint = ext == ".doc"
                ? "Ce fichier .doc est au format Word 97-2003. Enregistrez-le en .docx ou en PDF, puis reessayez."
                : "Fichier illisible. Verifiez qu'il n'est pas protege par un mot de passe, puis reessayez.";
            return new TextExtraction(null, hint, false);
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length < 50)
        {
            return new TextExtraction(null,
                "Aucun texte n'a pu etre lu. S'il s'agit d'un CV scanne (image), exportez-le en PDF texte ou en .docx.",
                false);
        }

        var truncated = text.Length > MaxCvTextLength;
        return new TextExtraction(truncated ? text[..MaxCvTextLength] : text, null, truncated);
    }

    // ═══ Text extraction ═══

    private static string ExtractTextFromPdf(Stream stream)
    {
        var sb = new StringBuilder();
        using var pdfReader = new PdfReader(stream);
        using var pdfDoc = new PdfDocument(pdfReader);

        for (int i = 1; i <= pdfDoc.GetNumberOfPages(); i++)
        {
            var page = pdfDoc.GetPage(i);
            // Lecture par position et non par ordre d'ecriture dans le fichier :
            // les CV a colonne laterale (competences a gauche, experiences a
            // droite) ressortent sinon entrelaces, une ligne sur deux.
            var strategy = new LocationTextExtractionStrategy();
            var text = PdfTextExtractor.GetTextFromPage(page, strategy);
            sb.AppendLine(text);
        }

        return sb.ToString();
    }

    private static string ExtractTextFromDocx(Stream stream)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;

        if (body == null) return string.Empty;

        // Extract all elements (paragraphs + tables)
        foreach (var element in body.ChildElements)
        {
            if (element is DocumentFormat.OpenXml.Wordprocessing.Paragraph para)
            {
                var text = para.InnerText?.Trim();
                if (!string.IsNullOrEmpty(text))
                    sb.AppendLine(text);
            }
            else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
            {
                foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
                {
                    var cells = new List<string>();
                    foreach (var cell in row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>())
                    {
                        var cellText = cell.InnerText?.Trim();
                        if (!string.IsNullOrEmpty(cellText))
                            cells.Add(cellText);
                    }
                    if (cells.Count > 0)
                        sb.AppendLine(string.Join(" | ", cells));
                }
            }
        }

        // Also extract from headers/footers if any
        if (doc.MainDocumentPart?.HeaderParts != null)
        {
            foreach (var header in doc.MainDocumentPart.HeaderParts)
            {
                var text = header.Header?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(text))
                    sb.Insert(0, text + "\n");
            }
        }

        return sb.ToString();
    }

    private static string BuildParsePrompt(string cvText)
    {
        return $@"Tu dois analyser le texte brut extrait d'un fichier CV (PDF ou Word) et en extraire TOUTES les informations pour remplir un CV en ligne structure.

══════════════════════════════
TEXTE BRUT DU CV :
══════════════════════════════
{cvText}
══════════════════════════════

OBJECTIF : Extraire EXHAUSTIVEMENT toutes les donnees du CV dans les 6 categories suivantes. Tu DOIS remplir CHAQUE categorie si l'information existe dans le texte. Ne laisse AUCUNE categorie vide si le CV contient des informations correspondantes.

LES 6 CATEGORIES OBLIGATOIRES :

1. ""Experience"" — Experiences professionnelles
   Extrais CHAQUE poste occupe : intitule, entreprise, ville, dates debut/fin, description detaillee des missions et realisations.
   Inclus aussi : stages, alternances, missions freelance, benevolat, jobs etudiants.

2. ""Formation"" — Formations et diplomes
   Extrais CHAQUE diplome ou formation : intitule du diplome, etablissement, ville, dates, description ou specialisation.
   Inclus aussi : certifications, formations en ligne, MOOC, permis.

3. ""Langue"" — Langues parlees
   Extrais CHAQUE langue mentionnee avec son niveau.
   Si aucune langue n'est explicitement mentionnee, ajoute au minimum ""Francais"" avec le niveau ""Natif"".
   Niveaux possibles : ""Natif"", ""Courant"", ""Avance (C1)"", ""Intermediaire (B2)"", ""Elementaire (A2)"", ""Debutant (A1)""

4. ""Competence"" — Competences techniques et transversales
   Cree UNE section par competence individuelle (ne pas les regrouper).
   Extrais TOUTES les competences : langages de programmation, outils, logiciels, frameworks, methodologies, soft skills.
   Niveaux possibles : ""Expert"", ""Avance"", ""Intermediaire"", ""Debutant""
   Si le niveau n'est pas mentionne, estime-le en fonction du contexte (experience, nombre d'annees).

5. ""CentreInteret"" — Centres d'interet et loisirs
   Extrais CHAQUE hobby, sport, activite associative, passion mentionnee.

6. ""Projet"" — Projets personnels ou academiques
   Extrais CHAQUE projet mentionne : projets perso, projets de fin d'etudes, contributions open source, projets academiques.

FORMAT DE SORTIE — JSON strict :
{{
  ""sections"": [
    {{
      ""sectionType"": ""Experience"",
      ""title"": ""Developpeur Full Stack"",
      ""organization"": ""Societe ABC"",
      ""location"": ""Paris"",
      ""startDate"": ""2022-03-01"",
      ""endDate"": null,
      ""description"": ""Developpement d'applications web en React et Node.js. Mise en place de pipelines CI/CD. Amelioration des performances de 40%."",
      ""level"": null,
      ""sortOrder"": 0
    }},
    {{
      ""sectionType"": ""Langue"",
      ""title"": ""Anglais"",
      ""organization"": null,
      ""location"": null,
      ""startDate"": null,
      ""endDate"": null,
      ""description"": null,
      ""level"": ""Courant"",
      ""sortOrder"": 0
    }},
    {{
      ""sectionType"": ""Competence"",
      ""title"": ""Python"",
      ""organization"": null,
      ""location"": null,
      ""startDate"": null,
      ""endDate"": null,
      ""description"": null,
      ""level"": ""Expert"",
      ""sortOrder"": 0
    }}
  ]
}}

REGLES STRICTES :
- Extrais FIDELEMENT les donnees du CV. Ne rien inventer.
- Si une date precise n'est pas mentionnee, approxime au 1er janvier de l'annee (""2022-01-01"").
- Si seule l'annee est donnee (""2020 - 2023""), utilise ""2020-01-01"" et ""2023-01-01"".
- endDate = null signifie ""poste actuel"" ou ""en cours"".
- Pour les competences : cree une section SEPAREE par competence (""Python"", ""SQL"", ""Docker"" = 3 sections distinctes).
- Pour les langues : cree une section SEPAREE par langue.
- sortOrder commence a 0 et incremente dans chaque categorie (les plus recents en premier pour Experience et Formation).
- Les descriptions doivent etre detaillees : reprends les missions, chiffres, realisations mentionnees dans le CV.
- Si le CV mentionne des informations de contact (email, telephone, adresse), IGNORE-les (elles sont gerees ailleurs).
- Reponds UNIQUEMENT avec le JSON. Pas de texte avant, pas de texte apres, pas de markdown.";
    }

    private string BuildPrompt(AppUser user, string? additionalContext)
    {
        return $@"A partir des informations suivantes sur un candidat, genere un CV structure complet en francais.

Informations du candidat:
- Nom: {user.FirstName} {user.LastName}
- Poste actuel: {user.Title ?? "Non renseigne"}
- Competences: {user.Skills ?? "Non renseignees"}
- Experience: {user.ExperienceYears?.ToString() ?? "Non renseigne"} ans
- Formation: {user.Education ?? "Non renseignee"}
- Ville: {user.City ?? "Non renseignee"}
- Bio: {user.Bio ?? "Non renseignee"}
{(string.IsNullOrEmpty(additionalContext) ? "" : $"- Instructions supplementaires: {additionalContext}")}

Genere un JSON object avec une cle ""sections"" contenant un array. Chaque element a ces champs:
- sectionType: ""Experience"" | ""Formation"" | ""Langue"" | ""Competence"" | ""CentreInteret"" | ""Projet""
- title: string
- organization: string ou null
- location: string ou null
- startDate: ""YYYY-MM-DD"" ou null
- endDate: ""YYYY-MM-DD"" ou null (null = en cours)
- description: string (2-3 phrases detaillees et professionnelles)
- level: string ou null (pour Langue: ""Natif"", ""Courant"", ""Avance"", ""Intermediaire"", ""Debutant""; pour Competence: ""Expert"", ""Avance"", ""Intermediaire"")
- sortOrder: int (ordre dans la section, commencant a 0)

Genere au minimum:
- 2-3 experiences professionnelles coherentes avec le profil et les annees d'experience
- 1-2 formations coherentes avec le diplome mentionne
- 2-3 langues (dont Francais natif)
- 4-6 competences techniques basees sur les skills
- 1-2 centres d'interet realistes
- 1 projet personnel si pertinent

Les descriptions doivent etre concretes, avec des chiffres et des realisations quand possible.
Reponds UNIQUEMENT avec le JSON, sans markdown, sans explication.";
    }

    private CvSection MapFromDto(CvSectionCreateDto dto) => new()
    {
        UserId = GetUserId(),
        SectionType = dto.SectionType,
        Title = dto.Title,
        Organization = dto.Organization,
        Location = dto.Location,
        StartDate = dto.StartDate,
        EndDate = dto.EndDate,
        Description = dto.Description,
        Level = dto.Level,
        SortOrder = dto.SortOrder
    };
}
