using DataControls.Core.Catalog;
using DataControls.Core.Operators;
using DataControls.Core.Paths;
using DataControls.Core.Validation;

namespace DataControls.Application.Catalog;

/// <summary>Serves the catalog, the operator rules and the limits to the UI, so the client never hard-codes any of them.</summary>
public sealed class CatalogService(ICatalogProvider catalogProvider, ValidationLimits limits)
{
    public async Task<IReadOnlyList<EntityDto>> GetEntitiesAsync(CancellationToken cancellationToken)
    {
        var catalog = await catalogProvider.GetCatalogAsync(cancellationToken);

        return [.. catalog.Entities.Select(e => new EntityDto(
            e.Key, e.DisplayName, e.DisplayNamePlural, e.Description,
            [.. e.Fields.OrderBy(f => f.SortOrder).Select(f => new FieldDto(
                f.Key, f.DisplayName, f.Description, f.DataType, f.IsRequiredByBusiness, f.AllowedValues, f.Unit))],
            [.. e.Relations.Select(r => new RelationDto(r.Key, r.DisplayName, r.Description, r.ToEntityKey, r.Cardinality))],
            [.. FieldPathResolver.ReachableFields(catalog, e.Key, limits.MaxRelationHops).Select(f => new FieldPathDto(
                f.Path, f.DisplayName, f.Owner, f.Entity.Key, f.Field.Key))]))];
    }

    public LimitsDto GetLimits() => new(
        limits.MaxConditions,
        limits.MaxDepth,
        limits.MaxRelationHops,
        limits.MaxTextValueLength,
        limits.MaxSearchLength,
        limits.MaxNameLength,
        limits.MaxDescriptionLength,
        limits.MaxAiTextLength);

    public static IReadOnlyList<OperatorDto> GetOperators() =>
    [
        .. OperatorRegistry.All.Select(o => new OperatorDto(
            o.Key, o.Label, o.DateLabel, [.. o.AllowedTypes.Order()], o.Shape, o.AllowsFieldOperand)),
    ];
}
