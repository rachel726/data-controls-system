namespace DataControls.Core.Catalog;

/// <summary>A business object (e.g. "עובד") mapped to a physical table.</summary>
public sealed record EntityMeta
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string DisplayNamePlural { get; init; }

    /// <summary>Grammatical gender of the entity's name, for Hebrew wording ("בהם" / "בהן").</summary>
    public GrammaticalGender Gender { get; init; } = GrammaticalGender.Masculine;

    public string? Description { get; init; }
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    public required string PrimaryKeyColumn { get; init; }

    /// <summary>Key of the field shown as the record's title in results.</summary>
    public required string DisplayFieldKey { get; init; }

    public int SortOrder { get; init; }
    public IReadOnlyList<FieldMeta> Fields { get; init; } = [];
    public IReadOnlyList<RelationMeta> Relations { get; init; } = [];
}

/// <summary>A business attribute of an entity (e.g. "גיל"), mapped to a column (optionally through a transform).</summary>
public sealed record FieldMeta
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string SourceColumn { get; init; }
    public required DataType DataType { get; init; }
    public FieldTransform Transform { get; init; } = FieldTransform.None;

    /// <summary>Second column used by <see cref="FieldTransform.FullName"/>.</summary>
    public string? TransformArg { get; init; }

    public bool IsRequiredByBusiness { get; init; }
    public bool IsVisibleInResults { get; init; }
    public bool IsSearchable { get; init; }
    public IReadOnlyList<AllowedValue> AllowedValues { get; init; } = [];

    /// <summary>Display unit, e.g. "₪".</summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Wording of a <see cref="DataType.Boolean"/> field as a predicate of its entity: "פעיל" → "עובד פעיל".
    /// Set together with <see cref="FalseText"/> ("אינו פעיל"); null = generic wording ("עובד פעיל: כן").
    /// </summary>
    public string? TrueText { get; init; }

    /// <inheritdoc cref="TrueText"/>
    public string? FalseText { get; init; }

    public int SortOrder { get; init; }
}

/// <summary>
/// A navigable link between two entities. Join condition: <c>[to].ToColumn = [from].FromColumn</c>.
/// </summary>
public sealed record RelationMeta
{
    public required string Key { get; init; }
    public required string FromEntityKey { get; init; }
    public required string ToEntityKey { get; init; }
    public required string FromColumn { get; init; }
    public required string ToColumn { get; init; }
    public required Cardinality Cardinality { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
}
