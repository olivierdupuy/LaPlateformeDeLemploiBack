using System.Text.Json;
using System.Text.Json.Serialization;

namespace lpdeBack.Services;

/// <summary>
/// Lecture indulgente des reponses d'un modele de langage.
///
/// Un modele suit le schema demande « en general ». Sur un CV reel, Mistral
/// renvoie par exemple <c>"education": ["Master …", "Licence …"]</c> la ou le
/// contrat attend une chaine : la desserialisation stricte echoue et toute
/// l'analyse est perdue pour un detail de forme. Ces convertisseurs ramenent
/// les ecarts de type courants vers le type attendu.
/// </summary>
public static class FlexibleJson
{
    public static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new FlexibleStringConverter());
        options.Converters.Add(new FlexibleIntConverter());
        return options;
    }
}

/// <summary>Accepte une chaine, un nombre, un booleen ou une liste (aplatie).</summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.Number:
                return reader.TryGetInt64(out var whole)
                    ? whole.ToString()
                    : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);

            case JsonTokenType.True:
                return "Oui";
            case JsonTokenType.False:
                return "Non";
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.StartArray:
            {
                var parts = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var part = Read(ref reader, typeToConvert, options);
                    if (!string.IsNullOrWhiteSpace(part)) parts.Add(part);
                }
                return parts.Count == 0 ? null : string.Join(", ", parts);
            }

            case JsonTokenType.StartObject:
            {
                // Un objet la ou l'on attend du texte : on garde ses valeurs
                // textuelles plutot que de perdre l'information.
                var parts = new List<string>();
                using var document = JsonDocument.ParseValue(ref reader);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) parts.Add(value);
                    }
                }
                return parts.Count == 0 ? null : string.Join(" — ", parts);
            }

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>Accepte un entier ecrit en nombre ou en texte (« 9 », « 9 ans »).</summary>
public class FlexibleIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var value) ? value : (int?)reader.GetDouble();

            case JsonTokenType.String:
            {
                var raw = reader.GetString();
                if (string.IsNullOrWhiteSpace(raw)) return null;
                var digits = new string(raw.TakeWhile(char.IsDigit).ToArray());
                return int.TryParse(digits, out var parsed) ? parsed : null;
            }

            case JsonTokenType.Null:
                return null;

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value);
    }
}
