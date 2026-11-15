using System.Text.Json;
using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Operators;

namespace DataControls.Application.Ai;

// The wire contract with the AI service (ai-service/app/models, docs/ai-response.schema.json).
// Serialized with the API's JSON contract: camelCase, string enums ("integer", "manyToOne", "isEmpty"), strict.

/// <summary>
/// What the AI is told about the data: business names and meanings only - no tables, columns or rows -
/// and the longest relation path a field may take (the validator's limit).
/// </summary>
public sealed record SchemaContext(IReadOnlyList<EntityContext> Entities, IReadOnlyList<OperatorContext> Operators, int MaxRelationHops);

public sealed record EntityContext(
    string Key,
    string DisplayName,
    string DisplayNamePlural,
    string? Description,
    IReadOnlyList<FieldContext> Fields,
    IReadOnlyList<RelationContext> Relations);

/// <summary>A business attribute; a yes/no one carries how it is said of its record ("פעיל" / "אינו פעיל").</summary>
public sealed record FieldContext(
    string Key,
    string DisplayName,
    string? Description,
    DataType DataType,
    IReadOnlyList<AllowedValue> AllowedValues,
    string? Unit,
    string? TrueText,
    string? FalseText);

public sealed record RelationContext(string Key, string DisplayName, string? Description, string TargetEntity, Cardinality Cardinality);

public sealed record OperatorContext(
    ConditionOperator Key,
    string Label,
    IReadOnlyList<DataType> AllowedTypes,
    OperandShape Shape,
    bool AllowsFieldOperand);

public sealed record SuggestRequest(string Text, SchemaContext Context);

/// <summary>A ready-made request the UI offers as a one-click example - served by the AI service, which answers it.</summary>
public sealed record AiExample(string Label, string Text);

/// <summary>The AI's proposal, in its own format - translated by <see cref="AiSuggestionMapper"/>, never run as is.</summary>
public sealed record ControlSuggestion(
    string Name = "",
    string? Description = null,
    string? RootEntity = null,
    LogicalOperator Logic = LogicalOperator.And,
    IReadOnlyList<SuggestedCondition>? Conditions = null,
    IReadOnlyList<SuggestedGroup>? Groups = null,
    string Explanation = "",
    IReadOnlyList<string>? Assumptions = null,
    IReadOnlyList<string>? UnmappedTerms = null);

/// <summary>A value (scalar, or an array for a range / list) or another field to compare with.</summary>
public sealed record SuggestedCondition(string Field, ConditionOperator Operator, JsonElement? Value = null, string? CompareToField = null);

public sealed record SuggestedGroup(LogicalOperator Logic, IReadOnlyList<SuggestedCondition> Conditions);
