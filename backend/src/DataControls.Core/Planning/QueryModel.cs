using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Paths;

namespace DataControls.Core.Planning;

/// <summary>
/// The logical query for one control - WHAT to read, independent of any SQL dialect.
/// Produced by <see cref="QueryPlanner"/> from a validated definition; turned into SQL by a renderer.
/// Every table/column in here comes from the catalog; every value is a typed CLR value (never SQL text).
/// </summary>
public sealed record QueryModel(
    EntityMeta RootEntity,
    TableSource Root,
    IReadOnlyList<Join> Joins,
    GroupPredicate Filter,
    FieldRef Title,
    IReadOnlyList<OutputColumn> Columns,
    IReadOnlyList<ConditionFlag> Flags);

/// <summary>A table with its alias in the query, e.g. [biz].[Employees] AS r.</summary>
public sealed record TableSource(string Schema, string Table, string Alias, string PrimaryKeyColumn);

/// <summary>LEFT JOIN <see cref="Table"/> ON Table.<see cref="Column"/> = <see cref="ParentAlias"/>.<see cref="ParentColumn"/>.</summary>
public sealed record Join(TableSource Table, string Column, string ParentAlias, string ParentColumn, string RelationPathKey);

/// <summary>A catalog field read through a table alias. The renderer applies the field's transform.</summary>
public sealed record FieldRef(string Alias, FieldMeta Field);

// ---------- predicates ----------

public abstract record Predicate;

public sealed record GroupPredicate(LogicalOperator Logic, IReadOnlyList<Predicate> Children) : Predicate;

/// <summary><see cref="Left"/> &lt;op&gt; <see cref="Right"/>. <see cref="Flag"/> is null inside a related-records subquery.</summary>
public sealed record ComparisonPredicate(int? Flag, FieldRef Left, ConditionOperator Operator, PlannedOperand? Right) : Predicate;

/// <summary>
/// [NOT] EXISTS (SELECT 1 FROM <see cref="Table"/> [joins] WHERE Table.<see cref="Column"/> = ParentAlias.ParentColumn AND <see cref="Where"/>).
/// </summary>
public sealed record ExistsPredicate(
    int Flag,
    bool Negated,
    TableSource Table,
    string Column,
    string ParentAlias,
    string ParentColumn,
    IReadOnlyList<Join> Joins,
    GroupPredicate? Where) : Predicate;

// ---------- operands (typed values, never SQL) ----------

public abstract record PlannedOperand;

/// <summary>One value: string, long, decimal, DateOnly or bool.</summary>
public sealed record ValueOperand(object Value) : PlannedOperand;

public sealed record RangeOperand(object From, object To) : PlannedOperand;

public sealed record ListOperand(IReadOnlyList<object> Values) : PlannedOperand;

public sealed record FieldOperandRef(FieldRef Field) : PlannedOperand;

// ---------- result shape ----------

/// <summary>
/// A column of the result table. <see cref="Key"/> is the field path with '.' replaced by "__" (safe as a JS property),
/// <see cref="IsConditionField"/> marks fields used in the control's conditions.
/// </summary>
public sealed record OutputColumn(string Key, ResolvedField Field, FieldRef Expression, bool IsConditionField);

/// <summary>
/// One leaf of the root filter (a condition or a related-records condition). The query returns a 0/1 column per flag,
/// so the reason builder can tell exactly which conditions a violating record met.
/// </summary>
public sealed record ConditionFlag(
    int Index,
    string NodePath,
    FilterNode Node,
    ResolvedField? Field,
    string? ColumnKey,
    PlannedOperand? Operand,
    ResolvedField? CompareField,
    string? CompareColumnKey,
    RelationMeta? Relation,
    EntityMeta? RelatedEntity);
