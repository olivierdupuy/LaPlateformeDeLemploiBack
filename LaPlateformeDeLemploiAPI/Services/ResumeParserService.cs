using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LaPlateformeDeLemploiAPI.DTOs;
using OpenAI.Chat;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace LaPlateformeDeLemploiAPI.Services;

public class ResumeParserService
{
    private readonly string _openAiKey;

    public ResumeParserService(IConfiguration config)
    {
        _openAiKey = config["OpenAI:ApiKey"] ?? "";
    }

    public string ExtractText(Stream fileStream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ExtractFromPdf(fileStream),
            ".docx" or ".doc" => ExtractFromDocx(fileStream),
            ".txt" => ExtractFromTxt(fileStream),
            _ => throw new ArgumentException($"Format non supporte : {ext}")
        };
    }

    // ========== PDF — extraction avancee avec gestion des colonnes ==========
    private string ExtractFromPdf(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        using var document = PdfDocument.Open(ms);
        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            // Recuperer tous les mots avec leurs positions
            var words = page.GetWords().ToList();
            if (words.Count == 0) continue;

            // Regrouper par lignes (meme Y a +/- 3 points)
            var lines = new List<List<Word>>();
            var sortedWords = words.OrderBy(w => -w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();

            List<Word>? currentLine = null;
            double currentY = double.MinValue;

            foreach (var word in sortedWords)
            {
                var y = Math.Round(word.BoundingBox.Bottom, 0);
                if (currentLine == null || Math.Abs(y - currentY) > 3)
                {
                    currentLine = new List<Word>();
                    lines.Add(currentLine);
                    currentY = y;
                }
                currentLine.Add(word);
            }

            // Reconstituer le texte ligne par ligne
            foreach (var line in lines)
            {
                var orderedWords = line.OrderBy(w => w.BoundingBox.Left).ToList();
                var lineText = new StringBuilder();

                for (int i = 0; i < orderedWords.Count; i++)
                {
                    if (i > 0)
                    {
                        var gap = orderedWords[i].BoundingBox.Left - orderedWords[i - 1].BoundingBox.Right;
                        // Grand espace = probablement une colonne, on met un separateur
                        lineText.Append(gap > 30 ? "  |  " : " ");
                    }
                    lineText.Append(orderedWords[i].Text);
                }

                var text = lineText.ToString().Trim();
                if (!string.IsNullOrEmpty(text))
                    sb.AppendLine(text);
            }

            sb.AppendLine(); // Separateur de page
        }

        return CleanExtractedText(sb.ToString());
    }

    // ========== DOCX — extraction complete avec structure ==========
    private string ExtractFromDocx(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";

        var sb = new StringBuilder();

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph para:
                    ProcessParagraph(para, sb);
                    break;

                case Table table:
                    ProcessTable(table, sb);
                    break;

                case SdtBlock sdt:
                    // Structured Document Tags (content controls)
                    foreach (var child in sdt.Descendants<Paragraph>())
                        ProcessParagraph(child, sb);
                    foreach (var child in sdt.Descendants<Table>())
                        ProcessTable(child, sb);
                    break;
            }
        }

        return CleanExtractedText(sb.ToString());
    }

    private void ProcessParagraph(Paragraph para, StringBuilder sb)
    {
        var text = GetParagraphText(para);
        if (string.IsNullOrWhiteSpace(text)) return;

        // Detecter les titres/headers via le style
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
        var isHeading = styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                     || styleId.StartsWith("Titre", StringComparison.OrdinalIgnoreCase);

        // Detecter le bold comme potentiel titre de section
        var isBold = para.Descendants<Bold>().Any()
                  || para.Descendants<RunProperties>().Any(rp => rp.Bold != null && (rp.Bold.Val == null || rp.Bold.Val));

        // Detecter les listes
        var isListItem = para.ParagraphProperties?.NumberingProperties != null;

        if (isHeading)
        {
            sb.AppendLine();
            sb.AppendLine($"=== {text.ToUpper()} ===");
        }
        else if (isBold && text.Length < 100 && !text.Contains(','))
        {
            // Probablement un sous-titre ou une section
            sb.AppendLine();
            sb.AppendLine($"--- {text} ---");
        }
        else if (isListItem)
        {
            sb.AppendLine($"  - {text}");
        }
        else
        {
            sb.AppendLine(text);
        }
    }

    private string GetParagraphText(Paragraph para)
    {
        var sb = new StringBuilder();
        foreach (var run in para.Descendants<Run>())
        {
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case DocumentFormat.OpenXml.Wordprocessing.Text t:
                        sb.Append(t.Text);
                        break;
                    case TabChar:
                        sb.Append("  ");
                        break;
                    case Break br:
                        sb.Append(br.Type?.Value == BreakValues.Page ? "\n\n" : "\n");
                        break;
                }
            }
        }

        // Gerer aussi les champs (dates, etc.)
        foreach (var field in para.Descendants<FieldCode>())
        {
            // Les champs Word sont souvent des dates ou numeros de page
        }

        return sb.ToString().Trim();
    }

    private void ProcessTable(Table table, StringBuilder sb)
    {
        sb.AppendLine();
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().ToList();
            var cellTexts = new List<string>();

            foreach (var cell in cells)
            {
                var cellText = new StringBuilder();
                foreach (var para in cell.Elements<Paragraph>())
                {
                    var text = GetParagraphText(para);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (cellText.Length > 0) cellText.Append(" | ");
                        cellText.Append(text);
                    }
                }
                cellTexts.Add(cellText.ToString().Trim());
            }

            var rowText = string.Join("  |  ", cellTexts.Where(c => !string.IsNullOrEmpty(c)));
            if (!string.IsNullOrWhiteSpace(rowText))
                sb.AppendLine(rowText);
        }
        sb.AppendLine();
    }

    private string ExtractFromTxt(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return CleanExtractedText(reader.ReadToEnd());
    }

    // ========== Nettoyage du texte ==========
    private string CleanExtractedText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // Supprimer les caracteres de controle sauf newlines et tabs
        text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");

        // Normaliser les sauts de ligne
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Supprimer les lignes avec seulement des tirets, underscores, etc.
        text = Regex.Replace(text, @"^[\s\-_=\*\.]{3,}$", "", RegexOptions.Multiline);

        // Reduire les lignes vides multiples a 2 max
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        // Supprimer les espaces en fin de ligne
        text = Regex.Replace(text, @"[ \t]+$", "", RegexOptions.Multiline);

        // Tronquer a 12000 caracteres pour ne pas depasser les limites de l'IA
        if (text.Length > 12000)
            text = text[..12000] + "\n[... texte tronque ...]";

        return text.Trim();
    }

    // ========== Analyse IA ==========
    public async Task<ResumeSaveDto?> ParseWithAI(string rawText)
    {
        if (string.IsNullOrEmpty(_openAiKey))
            throw new InvalidOperationException("Cle API OpenAI non configuree.");

        var client = new ChatClient("gpt-4o-mini", _openAiKey);

        var systemPrompt = @"Tu es un parseur de CV expert et infaillible. Ton travail est d'extraire TOUTES les informations d'un CV brut et de les structurer en JSON.

REGLES STRICTES :
1. DATES : Toujours au format yyyy-MM-dd. Si ""2020"" → ""2020-01-01"". Si ""Mars 2021"" ou ""03/2021"" → ""2021-03-01"". Si ""Depuis 2022"" ou ""Present"" ou ""Actuel"" → isCurrent=true, endDate=null.
2. EXPERIENCES : Extrais TOUTES les experiences professionnelles, stages, missions freelance, alternances. Si une experience n'a pas de date exacte, estime-la.
3. FORMATIONS : Extrais TOUS les diplomes, certifications, formations. Bac, BTS, Licence, Master, Ingenieur, Bootcamp, certifications (AWS, Scrum, etc.)
4. COMPETENCES : Extrais TOUTES les competences techniques ET soft skills. Niveaux: 1=Debutant 2=Junior 3=Intermediaire 4=Avance 5=Expert. Estime selon l'experience globale du candidat.
5. LANGUES : Extrais toutes les langues. Niveaux: Debutant, Intermediaire, Avance, Bilingue, Natif. Si ""Anglais courant"" → Avance. Si ""Anglais professionnel"" → Avance. Si ""Anglais bilingue"" → Bilingue.
6. TITRE : Genere un titre professionnel pertinent basé sur le profil (ex: ""Developpeur Full Stack Python / React - 5 ans d'experience"").
7. RESUME : Genere un resume professionnel de 2-3 phrases synthetisant le profil, les annees d'experience et les specialites.
8. Le texte peut etre mal formate (colonnes melangees, tableaux aplatis, symboles |). Fais preuve d'intelligence pour reconstituer le sens.
9. Si un texte semble etre un en-tete de section (entre === ou ---), c'est un indicateur de section.
10. Retourne UNIQUEMENT du JSON valide sans markdown, sans commentaires, sans backticks.";

        var userPrompt = @"Analyse ce CV et retourne le JSON structure :

{
  ""title"": ""string"",
  ""summary"": ""string"",
  ""experiences"": [
    { ""jobTitle"": ""string"", ""company"": ""string"", ""location"": ""string|null"", ""startDate"": ""yyyy-MM-dd"", ""endDate"": ""yyyy-MM-dd|null"", ""isCurrent"": false, ""description"": ""string"", ""order"": 0 }
  ],
  ""educations"": [
    { ""degree"": ""string"", ""school"": ""string"", ""location"": ""string|null"", ""startDate"": ""yyyy-MM-dd"", ""endDate"": ""yyyy-MM-dd|null"", ""isCurrent"": false, ""description"": ""string|null"", ""order"": 0 }
  ],
  ""skills"": [
    { ""name"": ""string"", ""level"": 3, ""order"": 0 }
  ],
  ""languages"": [
    { ""name"": ""string"", ""level"": ""Intermediaire"", ""order"": 0 }
  ]
}

TEXTE BRUT DU CV :
---
" + rawText;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.1f, // Tres factuel, peu creatif
            MaxOutputTokenCount = 4000
        };

        var result = await client.CompleteChatAsync(messages, options);
        var json = result.Value.Content[0].Text;

        // Nettoyage agressif du JSON
        json = CleanJsonResponse(json);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        try
        {
            return JsonSerializer.Deserialize<ResumeSaveDto>(json, jsonOptions);
        }
        catch (JsonException)
        {
            // Tentative de reparation du JSON
            json = TryFixJson(json);
            return JsonSerializer.Deserialize<ResumeSaveDto>(json, jsonOptions);
        }
    }

    private string CleanJsonResponse(string json)
    {
        // Enlever les backticks markdown
        json = Regex.Replace(json, @"^```(?:json)?\s*", "", RegexOptions.Multiline);
        json = Regex.Replace(json, @"\s*```\s*$", "", RegexOptions.Multiline);

        // Trouver le premier { et le dernier }
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start >= 0 && end > start)
            json = json[start..(end + 1)];

        return json.Trim();
    }

    private string TryFixJson(string json)
    {
        // Corriger les virgules trailing avant }
        json = Regex.Replace(json, @",\s*}", "}");
        json = Regex.Replace(json, @",\s*]", "]");

        // Corriger les guillemets manquants sur les cles
        json = Regex.Replace(json, @"(\w+)\s*:", "\"$1\":");

        // Supprimer les caracteres non-JSON
        json = Regex.Replace(json, @"[\x00-\x1F]", " ");

        return json;
    }
}
