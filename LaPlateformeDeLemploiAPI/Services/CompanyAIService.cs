using System.Text.Json;
using OpenAI.Chat;

namespace LaPlateformeDeLemploiAPI.Services;

public class CompanyAIService
{
    private readonly string _openAiKey;

    public CompanyAIService(IConfiguration config)
    {
        _openAiKey = Environment.GetEnvironmentVariable("OPENAI_SECRET_PAIKEY")
                     ?? config["OpenAI:ApiKey"]
                     ?? "";
    }

    private ChatClient GetClient() =>
        string.IsNullOrEmpty(_openAiKey)
            ? throw new InvalidOperationException("Cle API OpenAI non configuree.")
            : new ChatClient("gpt-4o-mini", _openAiKey);

    // ========== 1. Generateur d'offre d'emploi ==========
    public async Task<GeneratedJobOffer> GenerateJobDescription(string title, string? keywords, string? companyName, string? contractType)
    {
        var client = GetClient();

        var prompt = $@"Tu es un expert RH francais specialise dans la redaction d'offres d'emploi attractives et professionnelles.

Genere une offre d'emploi complete a partir des informations suivantes :
- Poste : {title}
- Mots-cles / technologies : {keywords ?? "non precise"}
- Entreprise : {companyName ?? "non precise"}
- Type de contrat : {contractType ?? "CDI"}

Retourne UNIQUEMENT du JSON valide (sans markdown, sans backticks) avec ce format :
{{
  ""title"": ""Titre optimise du poste"",
  ""description"": ""Description complete et professionnelle de 200 a 400 mots incluant : presentation du poste, missions principales (en liste), profil recherche, competences requises, ce que l'entreprise offre. Utilise des retours a la ligne pour structurer."",
  ""salaryMin"": nombre_entier_ou_null,
  ""salaryMax"": nombre_entier_ou_null,
  ""suggestedSkills"": [""skill1"", ""skill2"", ...]
}}

Pour les salaires, estime une fourchette realiste pour le marche francais en euros bruts annuels. Si stage, indique le salaire mensuel.";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Tu es un assistant RH expert. Reponds toujours en JSON valide, sans markdown."),
            new UserChatMessage(prompt)
        };

        var options = new ChatCompletionOptions { Temperature = 0.7f, MaxOutputTokenCount = 2000 };
        var result = await client.CompleteChatAsync(messages, options);
        var json = CleanJson(result.Value.Content[0].Text);

        return JsonSerializer.Deserialize<GeneratedJobOffer>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new Exception("Impossible de parser la reponse IA.");
    }

    // ========== 2. Scoring de candidat ==========
    public async Task<CandidateScore> ScoreCandidate(string jobTitle, string jobDescription, string candidateName, string? candidateSkills, string? candidateBio, string? coverLetter)
    {
        var client = GetClient();

        var prompt = $@"Tu es un recruteur expert. Analyse la compatibilite entre cette offre et ce candidat.

OFFRE :
- Poste : {jobTitle}
- Description : {jobDescription}

CANDIDAT :
- Nom : {candidateName}
- Competences : {candidateSkills ?? "Non renseignees"}
- Bio : {candidateBio ?? "Non renseignee"}
- Lettre de motivation : {coverLetter ?? "Aucune"}

Retourne UNIQUEMENT du JSON valide :
{{
  ""score"": nombre_entre_0_et_100,
  ""summary"": ""Resume en 2 phrases de la compatibilite"",
  ""strengths"": [""point fort 1"", ""point fort 2"", ...],
  ""weaknesses"": [""point faible ou manque 1"", ...],
  ""recommendation"": ""STRONG_YES|YES|MAYBE|NO"",
  ""recommendationLabel"": ""Fortement recommande|Recommande|A considerer|Pas adapte""
}}";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Tu es un recruteur senior. Evalue objectivement. Reponds en JSON sans markdown."),
            new UserChatMessage(prompt)
        };

        var options = new ChatCompletionOptions { Temperature = 0.2f, MaxOutputTokenCount = 1000 };
        var result = await client.CompleteChatAsync(messages, options);
        var json = CleanJson(result.Value.Content[0].Text);

        return JsonSerializer.Deserialize<CandidateScore>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new Exception("Impossible de parser le score.");
    }

    // ========== 3. Generateur d'email ==========
    public async Task<GeneratedEmail> GenerateEmail(string type, string candidateName, string jobTitle, string companyName, string? additionalInfo)
    {
        var client = GetClient();

        var typeDescription = type switch
        {
            "interview" => "invitation a un entretien",
            "accepted" => "acceptation de candidature / proposition d'embauche",
            "rejected" => "refus de candidature poli et encourageant",
            "info" => "demande d'informations complementaires",
            _ => "reponse professionnelle"
        };

        var prompt = $@"Redige un email professionnel en francais de type ""{typeDescription}"".

Contexte :
- Candidat : {candidateName}
- Poste : {jobTitle}
- Entreprise : {companyName}
- Informations supplementaires : {additionalInfo ?? "aucune"}

Retourne UNIQUEMENT du JSON valide :
{{
  ""subject"": ""Objet de l'email"",
  ""body"": ""Corps de l'email complet avec formule de politesse. Utilise \\n pour les retours a la ligne.""
}}";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Tu rediges des emails RH professionnels, chaleureux et respectueux. JSON sans markdown."),
            new UserChatMessage(prompt)
        };

        var options = new ChatCompletionOptions { Temperature = 0.6f, MaxOutputTokenCount = 1000 };
        var result = await client.CompleteChatAsync(messages, options);
        var json = CleanJson(result.Value.Content[0].Text);

        return JsonSerializer.Deserialize<GeneratedEmail>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new Exception("Impossible de parser l'email.");
    }

    private string CleanJson(string json)
    {
        json = System.Text.RegularExpressions.Regex.Replace(json, @"^```(?:json)?\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        json = System.Text.RegularExpressions.Regex.Replace(json, @"\s*```\s*$", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start >= 0 && end > start) json = json[start..(end + 1)];
        return json.Trim();
    }
}

// DTOs pour les reponses IA
public class GeneratedJobOffer
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public List<string> SuggestedSkills { get; set; } = new();
}

public class CandidateScore
{
    public int Score { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Strengths { get; set; } = new();
    public List<string> Weaknesses { get; set; } = new();
    public string Recommendation { get; set; } = "";
    public string RecommendationLabel { get; set; } = "";
}

public class GeneratedEmail
{
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
}
