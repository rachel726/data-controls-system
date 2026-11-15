using DataControls.Core.Validation;

namespace DataControls.Application.Common;

/// <summary>The requested item does not exist (or was deleted). Message is user-facing Hebrew.</summary>
public sealed class NotFoundException(string message) : Exception(message)
{
    public static NotFoundException Control(int id) => new($"הבקרה המבוקשת (מספר {id}) לא נמצאה. ייתכן שנמחקה.");
}

/// <summary>Simple request fields are invalid (e.g. an empty name). Errors are keyed by field name.</summary>
public sealed class InputValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("הנתונים שהוזנו אינם תקינים.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>The control definition does not pass the validator. <see cref="Errors"/> point at the offending nodes.</summary>
public sealed class DefinitionInvalidException(IReadOnlyList<ValidationError> errors, string message = "הגדרת הבקרה אינה תקינה.")
    : Exception(message)
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}
