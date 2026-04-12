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

namespace lpdeBack.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Candidate")]
public class CvController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly string[] ValidTypes = { "Experience", "Formation", "Langue", "Competence", "CentreInteret", "Projet" };

    public CvController(AppDbContext context, IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

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

    // ═══ OpenAI Generation ═══

    [HttpPost("generate-ai")]
    public async Task<ActionResult<List<CvSectionCreateDto>>> GenerateWithAi(AiGenerateRequestDto? dto)
    {
        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return StatusCode(503, new { message = "Cle API OpenAI non configuree. Ajoutez OpenAI:ApiKey dans appsettings.json." });

        var user = await _context.Users.FindAsync(GetUserId());
        if (user == null) return Unauthorized();

        var prompt = BuildPrompt(user, dto?.AdditionalContext);

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new object[]
                {
                    new { role = "system", content = "Tu es un assistant RH expert en redaction de CV professionnels en francais. Reponds UNIQUEMENT avec un JSON valide." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.7,
                max_tokens = 3000
            };

            var response = await client.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode(502, new { message = $"Erreur API OpenAI: {response.StatusCode}", detail = error });
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrEmpty(content))
                return StatusCode(502, new { message = "Reponse vide de l'IA." });

            // Parse the JSON response — handle both array and object with "sections" key
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<CvSectionCreateDto>? sections;

            try
            {
                // Try parsing as { "sections": [...] }
                var wrapper = JsonSerializer.Deserialize<AiGenerateResponseDto>(content, options);
                sections = wrapper?.Sections;

                // If no sections found, try parsing as direct array
                if (sections == null || sections.Count == 0)
                    sections = JsonSerializer.Deserialize<List<CvSectionCreateDto>>(content, options);
            }
            catch
            {
                // Fallback: try to extract JSON array from the content
                var start = content.IndexOf('[');
                var end = content.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    var arrayJson = content[start..(end + 1)];
                    sections = JsonSerializer.Deserialize<List<CvSectionCreateDto>>(arrayJson, options);
                }
                else
                {
                    return BadRequest(new { message = "Impossible de parser la reponse de l'IA. Reessayez." });
                }
            }

            if (sections == null || sections.Count == 0)
                return BadRequest(new { message = "L'IA n'a genere aucune section. Reessayez." });

            // Validate section types
            sections = sections.Where(s => ValidTypes.Contains(s.SectionType)).ToList();

            return Ok(sections);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Erreur lors de la generation IA.", detail = ex.Message });
        }
    }

    // ═══ Parse CV File (PDF/DOCX/DOC) via OpenAI ═══

    [HttpPost("parse-file")]
    public async Task<ActionResult<List<CvSectionCreateDto>>> ParseCvFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Aucun fichier envoye." });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "Le fichier ne doit pas depasser 10 Mo." });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".pdf" && ext != ".docx" && ext != ".doc")
            return BadRequest(new { message = "Formats acceptes : PDF, DOCX, DOC." });

        var apiKey = _config["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return StatusCode(503, new { message = "Cle API OpenAI non configuree." });

        // Extract text from file (copy to MemoryStream for seekable access)
        string extractedText;
        try
        {
            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            extractedText = ext == ".pdf" ? ExtractTextFromPdf(memoryStream) : ExtractTextFromDocx(memoryStream);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Impossible de lire le fichier : {ex.Message}" });
        }

        if (string.IsNullOrWhiteSpace(extractedText) || extractedText.Length < 50)
            return BadRequest(new { message = "Le fichier ne contient pas assez de texte exploitable." });

        // Truncate if too long (OpenAI token limit)
        if (extractedText.Length > 12000)
            extractedText = extractedText[..12000];

        // Build prompt for CV parsing
        var prompt = BuildParsePrompt(extractedText);

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new object[]
                {
                    new { role = "system", content = "Tu es un expert RH specialise dans l'analyse et la structuration de CV professionnels. Tu extrais TOUTES les informations d'un CV de maniere exhaustive et fidelele. Tu reponds UNIQUEMENT en JSON valide, sans aucun texte autour." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                max_tokens = 6000
            };

            var response = await client.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode(502, new { message = $"Erreur API OpenAI: {response.StatusCode}" });
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            if (string.IsNullOrEmpty(content))
                return StatusCode(502, new { message = "Reponse vide de l'IA." });

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<CvSectionCreateDto>? sections;

            try
            {
                var wrapper = JsonSerializer.Deserialize<AiGenerateResponseDto>(content, options);
                sections = wrapper?.Sections;
                if (sections == null || sections.Count == 0)
                    sections = JsonSerializer.Deserialize<List<CvSectionCreateDto>>(content, options);
            }
            catch
            {
                var start = content.IndexOf('[');
                var end = content.LastIndexOf(']');
                if (start >= 0 && end > start)
                    sections = JsonSerializer.Deserialize<List<CvSectionCreateDto>>(content[start..(end + 1)], options);
                else
                    return BadRequest(new { message = "Impossible de parser la reponse de l'IA. Reessayez." });
            }

            if (sections == null || sections.Count == 0)
                return BadRequest(new { message = "Aucune section extraite du CV. Verifiez le contenu du fichier." });

            sections = sections.Where(s => ValidTypes.Contains(s.SectionType)).ToList();
            return Ok(sections);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Erreur lors de l'analyse du CV.", detail = ex.Message });
        }
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
            var strategy = new SimpleTextExtractionStrategy();
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
