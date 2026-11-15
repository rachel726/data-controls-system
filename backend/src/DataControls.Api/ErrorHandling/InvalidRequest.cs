using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.ErrorHandling;

/// <summary>
/// Requests that cannot be read (malformed JSON, unknown properties, wrong types): the client gets Hebrew messages
/// per property - never the framework's text, which names internal .NET types - and the details go to the log.
/// </summary>
public static partial class InvalidRequest
{
    public static readonly IReadOnlyDictionary<int, string> StatusTitles = new Dictionary<int, string>
    {
        [StatusCodes.Status400BadRequest] = "הבקשה אינה תקינה.",
        [StatusCodes.Status404NotFound] = "הכתובת המבוקשת לא נמצאה.",
        [StatusCodes.Status405MethodNotAllowed] = "הפעולה אינה נתמכת בכתובת זו.",
        [StatusCodes.Status413PayloadTooLarge] = "הבקשה גדולה מדי.",
        [StatusCodes.Status415UnsupportedMediaType] = "סוג התוכן אינו נתמך - יש לשלוח JSON.",
    };

    public static IActionResult Problem(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var invalid = context.ModelState.Where(e => e.Value is { Errors.Count: > 0 }).ToList();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(InvalidRequest));
        LogInvalid(logger, context.HttpContext.Request.Path,
            string.Join("; ", invalid.SelectMany(e => e.Value!.Errors.Select(x => $"{e.Key}: {x.ErrorMessage}{x.Exception?.Message}"))));

        // JSON errors arrive as "$.property" (plus a generic entry for the whole body parameter, which adds nothing).
        var jsonErrors = invalid.Where(e => e.Key.StartsWith('$')).ToList();
        var errors = (jsonErrors.Count > 0 ? jsonErrors : invalid).ToDictionary(
            e => Property(e.Key),
            e => new[] { e.Key == "$" ? "גוף הבקשה אינו JSON תקין." : $"הערך של '{Property(e.Key)}' אינו תקין או אינו מוכר." });

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "הבקשה אינה תקינה.",
            Detail = "מבנה הנתונים שנשלחו אינו תקין.",
        };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem);   // MVC sets application/problem+json for ProblemDetails values
    }

    /// <summary>"$.definition.filter" → "definition.filter"; "$" → "body".</summary>
    private static string Property(string key) => key == "$" ? "body" : key.TrimStart('$', '.');

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unreadable request to {Path}: {Errors}")]
    private static partial void LogInvalid(ILogger logger, PathString path, string errors);
}
