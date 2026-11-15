using DataControls.Core.Catalog;
using DataControls.Core.Operators;
using DataControls.Core.Validation;

namespace DataControls.Application.Ai;

/// <summary>
/// Builds what the AI is told about the data from the same catalog the engine uses: entities, fields with their
/// business names, meanings, types, allowed values, units and yes/no wording, relations, and the operators. Physical names
/// (schema, table, column) and data rows are never included - the model works in business terms only.
/// The relation-path limit is the validator's, so the AI is told the same rule its answer is checked against.
/// </summary>
public static class SchemaContextBuilder
{
    public static SchemaContext Build(MetadataCatalog catalog, ValidationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(limits);

        return new(
            [.. catalog.Entities.Select(e => new EntityContext(
                e.Key, e.DisplayName, e.DisplayNamePlural, e.Description,
                [.. e.Fields.OrderBy(f => f.SortOrder).Select(f => new FieldContext(
                    f.Key, f.DisplayName, f.Description, f.DataType, f.AllowedValues, f.Unit, f.TrueText, f.FalseText))],
                [.. e.Relations.Select(r => new RelationContext(r.Key, r.DisplayName, r.Description, r.ToEntityKey, r.Cardinality))]))],
            [.. OperatorRegistry.All.Select(o => new OperatorContext(
                o.Operator, o.Label, [.. o.AllowedTypes.Order()], o.Shape, o.AllowsFieldOperand))],
            limits.MaxRelationHops);
    }
}
