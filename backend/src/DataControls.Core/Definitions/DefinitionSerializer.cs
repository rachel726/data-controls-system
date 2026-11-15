using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataControls.Core.Definitions;

/// <summary>
/// The single JSON contract for control definitions (stored in the DB, sent by the UI, produced from AI output).
/// Strict on purpose: unknown properties, unknown node types, unknown operators and numeric enum values are rejected -
/// a definition is never "guessed". Semantic checks (fields exist, types match) are the validator's job.
/// </summary>
public static class DefinitionSerializer
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(ControlDefinition definition) =>
        JsonSerializer.Serialize(definition, Options);

    /// <exception cref="DefinitionFormatException">The JSON is malformed or does not match the contract.</exception>
    public static ControlDefinition Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new DefinitionFormatException("הגדרת הבקרה ריקה.", "Definition JSON is empty.");
        }

        try
        {
            return JsonSerializer.Deserialize<ControlDefinition>(json, Options)
                ?? throw new DefinitionFormatException("הגדרת הבקרה ריקה.", "Definition JSON is null.");
        }
        catch (JsonException ex)
        {
            throw new DefinitionFormatException("מבנה הגדרת הבקרה אינו תקין.", ex.Message, ex);
        }
        catch (NotSupportedException ex)
        {
            // e.g. a node without "type" - System.Text.Json cannot create the abstract base
            throw new DefinitionFormatException("מבנה הגדרת הבקרה אינו תקין.", ex.Message, ex);
        }
    }

    /// <summary>
    /// Applies the JSON contract rules to any options instance - used here and by the Web API,
    /// so stored definitions and API payloads can never drift apart.
    /// </summary>
    public static void ApplyContract(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = false;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        options.AllowOutOfOrderMetadataProperties = true;   // "type"/"kind" need not be the first property (AI output, hand-written JSON)
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        ApplyContract(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

/// <summary>The definition JSON could not be read. <see cref="Exception.Message"/> is user-facing Hebrew.</summary>
public sealed class DefinitionFormatException : Exception
{
    public DefinitionFormatException(string message, string technicalDetail, Exception? inner = null)
        : base(message, inner) => TechnicalDetail = technicalDetail;

    /// <summary>Technical reason (for logs / developers), e.g. the JSON path that failed.</summary>
    public string TechnicalDetail { get; }
}
