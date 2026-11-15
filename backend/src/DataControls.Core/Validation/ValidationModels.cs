namespace DataControls.Core.Validation;

/// <summary>
/// Every limit on what a user enters (PLAN §5.3) - one object, configuration section "Validation". The validator, the
/// services, the SQL renderer, the UI (GET /api/catalog/limits) and the AI context all read it; nothing repeats a limit.
/// </summary>
public sealed record ValidationLimits
{
    /// <summary>Nesting levels of groups / related conditions, the root group being level 1.</summary>
    public int MaxDepth { get; init; } = 3;

    /// <summary>Conditions + related-record conditions in one control.</summary>
    public int MaxConditions { get; init; } = 20;

    /// <summary>
    /// Many-to-one relations a field path may walk: 1 = the record's own fields and those of a directly related record
    /// ("department.isActive", "manager.salary") - every assignment example, and a short field list for the user.
    /// </summary>
    public int MaxRelationHops { get; init; } = 1;
    public int MaxListSize { get; init; } = 100;

    /// <summary>A text value in a condition, in characters.</summary>
    public int MaxTextValueLength { get; init; } = 200;

    /// <summary>Search and column-filter text on the results, in characters.</summary>
    public int MaxSearchLength { get; init; } = 100;

    public int MaxNameLength { get; init; } = 200;
    public int MaxDescriptionLength { get; init; } = 1000;

    /// <summary>A control described in free text for the AI, in characters.</summary>
    public int MaxAiTextLength { get; init; } = 1000;

    public static ValidationLimits Default { get; } = new();
}

public enum ValidationErrorCode
{
    // definition
    UnsupportedSchemaVersion,
    UnknownEntity,
    EmptyFilter,
    EmptyGroup,
    MissingNode,
    TooDeep,
    TooManyConditions,

    // field paths
    EmptyPath,
    InvalidPath,
    UnknownRelation,
    OneToManyInPath,
    TooManyRelations,
    RelationWithoutField,
    UnknownField,

    // operator / operand
    OperatorNotAllowedForType,
    OperandMissing,
    OperandNotExpected,
    FieldOperandNotAllowed,
    IncompatibleFieldTypes,
    SameField,
    InvalidValue,
    InvalidRange,
    EmptyList,
    ListTooLong,

    // related-records conditions
    RelationNotOneToMany,
    NestedRelatedNotAllowed,
}

/// <summary>
/// One problem in a definition. <see cref="Path"/> points at the offending node
/// (e.g. "filter.children[1].operand") so the UI can highlight the exact row; <see cref="Message"/> is user-facing Hebrew.
/// </summary>
public sealed record ValidationError(string Path, ValidationErrorCode Code, string Message)
{
    /// <summary>
    /// Only something not filled in yet - no field, no value, an empty list or group - not a mistake. While the user is
    /// still editing, the UI does not mark it; the definition stays invalid (it cannot be saved or run) until completed.
    /// </summary>
    public bool IsIncomplete => Code is ValidationErrorCode.EmptyPath or ValidationErrorCode.OperandMissing
        or ValidationErrorCode.EmptyList or ValidationErrorCode.EmptyGroup or ValidationErrorCode.MissingNode;
}

public sealed record ValidationResult(IReadOnlyList<ValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static ValidationResult Valid { get; } = new([]);
}
