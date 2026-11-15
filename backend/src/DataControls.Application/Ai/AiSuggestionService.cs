using DataControls.Application.Common;
using DataControls.Application.Controls;
using DataControls.Core.Catalog;
using DataControls.Core.Controls;
using DataControls.Core.Definitions;
using DataControls.Core.Describing;
using DataControls.Core.Validation;

namespace DataControls.Application.Ai;

public sealed record AiSuggestRequest(string? Text = null);

/// <summary>
/// The AI's proposal, ready for the user's approval: its own texts, the translated Stage A definition, and the
/// result of the same validation a hand-made control gets (with the Hebrew meaning when valid).
/// Nothing is saved or run here.
/// </summary>
public sealed record AiSuggestionResult(
    bool Understood,
    string Name,
    string? Description,
    string Explanation,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> UnmappedTerms,
    ControlDefinition? Definition,
    bool IsValid,
    IReadOnlyList<ValidationErrorDto> ValidationErrors,
    string? DefinitionDescription);

/// <summary>Free text → business schema context → AI → Stage A definition → validation + Hebrew meaning.</summary>
public sealed class AiSuggestionService(
    IControlSuggestionClient client, ICatalogProvider catalogProvider, IControlRepository repository, DefinitionValidator validator)
{
    /// <summary>The AI's one-click examples, as it serves them - the UI holds no request text of its own.</summary>
    public Task<IReadOnlyList<AiExample>> GetExamplesAsync(CancellationToken cancellationToken) => client.GetExamplesAsync(cancellationToken);

    public async Task<AiSuggestionResult> SuggestAsync(AiSuggestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw new InputValidationException(new Dictionary<string, string[]> { ["text"] = ["יש לתאר את הבקרה הרצויה."] });
        }
        if (text.Length > validator.Limits.MaxAiTextLength)
        {
            throw new InputValidationException(new Dictionary<string, string[]> { ["text"] = [$"התיאור ארוך מדי (עד {validator.Limits.MaxAiTextLength} תווים)."] });
        }

        var catalog = await catalogProvider.GetCatalogAsync(cancellationToken);
        var suggestion = await client.SuggestAsync(new SuggestRequest(text, SchemaContextBuilder.Build(catalog, validator.Limits)), cancellationToken);
        var definition = AiSuggestionMapper.Map(suggestion);

        if (definition is null)
        {
            return Result(suggestion, null, false, [], null);
        }

        suggestion = suggestion with { Name = await FreeNameAsync(suggestion.Name, cancellationToken) };

        var validation = validator.Validate(definition, catalog);
        return validation.IsValid
            ? Result(suggestion, definition, true, [], DefinitionDescriber.Describe(definition, catalog))
            : Result(suggestion, definition, false, [.. validation.Errors.Select(ValidationErrorDto.From)], null);
    }

    /// <summary>
    /// The suggested name if no control has it, else the first free "name (2)", "name (3)"... Control names are unique,
    /// so without this, approving would fail whenever the request matches a control that already exists - for example
    /// an assignment example whose sample control is in the list. The name can still be edited before approving.
    /// </summary>
    private async Task<string> FreeNameAsync(string name, CancellationToken cancellationToken)
    {
        name = name.Trim();
        var limit = validator.Limits.MaxNameLength;
        var stem = name[..Math.Min(name.Length, limit - 10)];   // room for " (n)"
        if (stem.Length == 0)
        {
            return name;
        }

        var taken = (await repository.ListAsync(stem, cancellationToken)).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name))
        {
            return name;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static AiSuggestionResult Result(
        ControlSuggestion suggestion, ControlDefinition? definition, bool isValid, IReadOnlyList<ValidationErrorDto> errors, string? meaning) =>
        new(
            Understood: definition is not null,
            suggestion.Name,
            suggestion.Description,
            suggestion.Explanation,
            suggestion.Assumptions ?? [],
            suggestion.UnmappedTerms ?? [],
            definition,
            isValid,
            errors,
            meaning);
}
