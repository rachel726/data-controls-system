using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataControls.Core.Definitions;

/// <summary>
/// A control, stored as data (never as SQL): which entity to check and the condition tree that
/// describes the PROBLEMATIC situation. Records of <see cref="RootEntity"/> that match <see cref="Filter"/> are violations.
/// Field paths are relative to the root entity: "age", "department.isActive", "manager.salary".
/// </summary>
public sealed record ControlDefinition(
    int SchemaVersion,
    string RootEntity,
    [property: JsonConverter(typeof(GroupNodeJsonConverter))] GroupNode Filter)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>A node of the condition tree. The JSON property "type" selects the concrete node.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(GroupNode), "group")]
[JsonDerivedType(typeof(ConditionNode), "condition")]
[JsonDerivedType(typeof(RelatedNode), "related")]
public abstract record FilterNode;

/// <summary>Combines child nodes with AND ("כל התנאים") or OR ("אחד מהתנאים").</summary>
public sealed record GroupNode(LogicalOperator Logic, IReadOnlyList<FilterNode> Children) : FilterNode;

/// <summary>A single comparison: field, operator and (unless the operator needs none) an operand.</summary>
public sealed record ConditionNode(string Field, ConditionOperator Operator, Operand? Operand = null) : FilterNode;

/// <summary>
/// "There is (any) / there is no (none) related record matching <see cref="Where"/>" over a one-to-many relation,
/// e.g. a department with no active employees. Paths inside <see cref="Where"/> are relative to the related entity.
/// </summary>
public sealed record RelatedNode(
    string Relation,
    RelatedQuantifier Quantifier,
    [property: JsonConverter(typeof(GroupNodeJsonConverter))] GroupNode? Where = null) : FilterNode;

/// <summary>Right-hand side of a condition. The JSON property "kind" selects the concrete operand.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(LiteralOperand), "literal")]
[JsonDerivedType(typeof(FieldOperand), "field")]
public abstract record Operand;

/// <summary>
/// A constant value exactly as written (number, string, boolean or array). It is checked and converted
/// against the field's data type by the validator - the JSON layer does not guess types.
/// </summary>
public sealed record LiteralOperand(JsonElement Value) : Operand;

/// <summary>Compare against another field of the same record (path relative to the root entity), e.g. "hireDate".</summary>
public sealed record FieldOperand(string Field) : Operand;

public enum LogicalOperator
{
    And,
    Or,
}

public enum RelatedQuantifier
{
    /// <summary>At least one related record matches.</summary>
    Any,

    /// <summary>No related record matches.</summary>
    None,
}

/// <summary>Comparison operators. Labels, allowed data types and operand kinds live in the operator registry.</summary>
public enum ConditionOperator
{
    Eq,
    Neq,
    Gt,
    Gte,
    Lt,
    Lte,
    Between,
    In,
    Contains,
    IsEmpty,
    IsNotEmpty,
}
