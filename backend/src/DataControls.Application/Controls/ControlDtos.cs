using DataControls.Core.Controls;
using DataControls.Core.Definitions;
using DataControls.Core.Validation;

namespace DataControls.Application.Controls;

/// <summary>Create / update request. All fields optional at the JSON level so missing ones get a Hebrew message from the service.</summary>
public sealed record SaveControlRequest(string? Name = null, string? Description = null, ControlDefinition? Definition = null);

public sealed record ControlListItemDto(
    int Id,
    string Name,
    string? Description,
    string RootEntityKey,
    string RootEntityName,
    DateTime UpdatedAt,
    LastRunDto? LastRun);

public sealed record ControlDto(
    int Id,
    string Name,
    string? Description,
    ControlDefinition Definition,
    string DefinitionDescription,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    LastRunDto? LastRun);

public sealed record LastRunDto(DateTime RunAt, RunStatus Status, int? ViolationCount, string? Error)
{
    public static LastRunDto? From(LastRun? run) => run is null ? null : new(run.RunAt, run.Status, run.ViolationCount, run.Error);
}

/// <summary>A validation error; <see cref="IsIncomplete"/> = only not filled in yet (see <see cref="ValidationError.IsIncomplete"/>).</summary>
public sealed record ValidationErrorDto(string Path, string Code, string Message, bool IsIncomplete)
{
    public static ValidationErrorDto From(ValidationError error) => new(error.Path, error.Code.ToString(), error.Message, error.IsIncomplete);
}

/// <summary>Result of checking a (possibly unsaved) definition: errors to highlight, or the Hebrew sentence it means.</summary>
public sealed record ValidateDefinitionResponse(bool IsValid, IReadOnlyList<ValidationErrorDto> Errors, string? Description);
