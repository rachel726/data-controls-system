using DataControls.Core.Definitions;

namespace DataControls.Application.Ai;

/// <summary>
/// Translates the AI's answer into a Stage A <see cref="ControlDefinition"/> - the one format the engine knows.
/// Pure translation: whether the fields / operators / values make sense is decided afterwards by the same
/// validator as for a control built by hand, so the AI gets no shortcut into the engine.
/// </summary>
public static class AiSuggestionMapper
{
    /// <returns>The definition, or null when the AI did not map the request to any condition.</returns>
    public static ControlDefinition? Map(ControlSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        var conditions = suggestion.Conditions ?? [];
        var groups = suggestion.Groups ?? [];
        if (suggestion.RootEntity is null || (conditions.Count == 0 && groups.Count == 0))
        {
            return null;
        }

        IReadOnlyList<FilterNode> children =
        [
            .. conditions.Select(Condition),
            .. groups.Select(g => new GroupNode(g.Logic, [.. g.Conditions.Select(Condition)])),
        ];
        return new ControlDefinition(ControlDefinition.CurrentSchemaVersion, suggestion.RootEntity, new GroupNode(suggestion.Logic, children));
    }

    private static ConditionNode Condition(SuggestedCondition condition) =>
        new(condition.Field, condition.Operator, condition switch
        {
            { CompareToField: { } field } => new FieldOperand(field),
            { Value: { } value } => new LiteralOperand(value.Clone()),   // detached from the response document
            _ => null,
        });
}
