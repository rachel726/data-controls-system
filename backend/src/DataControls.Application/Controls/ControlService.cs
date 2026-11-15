using DataControls.Application.Common;
using DataControls.Core.Catalog;
using DataControls.Core.Controls;
using DataControls.Core.Definitions;
using DataControls.Core.Describing;
using DataControls.Core.Validation;

namespace DataControls.Application.Controls;

/// <summary>
/// Use cases for managing controls: list, read, create, update, delete, and validate an unsaved definition.
/// Every definition is validated against the CURRENT catalog before it is stored.
/// </summary>
public sealed class ControlService(IControlRepository repository, ICatalogProvider catalogProvider, DefinitionValidator validator)
{

    public async Task<IReadOnlyList<ControlListItemDto>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var catalog = await catalogProvider.GetCatalogAsync(cancellationToken);
        var controls = await repository.ListAsync(search, cancellationToken);

        return [.. controls.Select(c => new ControlListItemDto(
            c.Id, c.Name, c.Description, c.RootEntityKey,
            catalog.TryGetEntity(c.RootEntityKey, out var entity) ? entity.DisplayNamePlural : c.RootEntityKey,
            c.UpdatedAt, LastRunDto.From(c.LastRun)))];
    }

    public async Task<ControlDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var control = await repository.GetAsync(id, cancellationToken) ?? throw NotFoundException.Control(id);
        var catalog = await catalogProvider.GetCatalogAsync(cancellationToken);

        return ToDto(control, catalog);
    }

    public async Task<ControlDto> CreateAsync(SaveControlRequest request, CancellationToken cancellationToken)
    {
        var (data, catalog) = await PrepareAsync(request, cancellationToken);
        var id = await repository.CreateAsync(data, cancellationToken);

        return ToDto((await repository.GetAsync(id, cancellationToken))!, catalog);
    }

    public async Task<ControlDto> UpdateAsync(int id, SaveControlRequest request, CancellationToken cancellationToken)
    {
        var (data, catalog) = await PrepareAsync(request, cancellationToken);
        if (!await repository.UpdateAsync(id, data, cancellationToken))
        {
            throw NotFoundException.Control(id);
        }

        return ToDto((await repository.GetAsync(id, cancellationToken))!, catalog);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!await repository.DeleteAsync(id, cancellationToken))
        {
            throw NotFoundException.Control(id);
        }
    }

    /// <summary>Checks an unsaved definition (live feedback in the editor and on the AI approval screen).</summary>
    public async Task<ValidateDefinitionResponse> ValidateAsync(ControlDefinition? definition, CancellationToken cancellationToken)
    {
        if (definition is null)
        {
            return new(false, [ValidationErrorDto.From(new("definition", ValidationErrorCode.EmptyFilter, "לא נשלחה הגדרת בקרה."))], null);
        }

        var catalog = await catalogProvider.GetCatalogAsync(cancellationToken);
        var result = validator.Validate(definition, catalog);

        return result.IsValid
            ? new(true, [], DefinitionDescriber.Describe(definition, catalog))
            : new(false, [.. result.Errors.Select(ValidationErrorDto.From)], null);
    }

    private async Task<(ControlData Data, MetadataCatalog Catalog)> PrepareAsync(SaveControlRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errors["name"] = ["יש להזין שם לבקרה."];
        }
        else if (name.Length > validator.Limits.MaxNameLength)
        {
            errors["name"] = [$"שם הבקרה ארוך מדי (עד {validator.Limits.MaxNameLength} תווים)."];
        }

        if (request.Description?.Trim().Length > validator.Limits.MaxDescriptionLength)
        {
            errors["description"] = [$"התיאור ארוך מדי (עד {validator.Limits.MaxDescriptionLength} תווים)."];
        }

        if (request.Definition is null)
        {
            errors["definition"] = ["יש להגדיר את תנאי הבקרה."];
        }

        if (errors.Count > 0)
        {
            throw new InputValidationException(errors);
        }

        var catalog = await catalogProvider.GetCatalogAsync(cancellationToken);
        var result = validator.Validate(request.Definition!, catalog);
        if (!result.IsValid)
        {
            throw new DefinitionInvalidException(result.Errors);
        }

        return (new ControlData(name!, request.Description, request.Definition!), catalog);
    }

    private ControlDto ToDto(Control control, MetadataCatalog catalog)
    {
        // A stored definition may have become invalid if the catalog changed since it was saved.
        var description = validator.Validate(control.Definition, catalog).IsValid
            ? DefinitionDescriber.Describe(control.Definition, catalog)
            : "הבקרה אינה תקינה עוד מול מבנה הנתונים הנוכחי - יש לערוך אותה.";

        return new(control.Id, control.Name, control.Description, control.Definition, description,
            control.CreatedAt, control.UpdatedAt, LastRunDto.From(control.LastRun));
    }
}
