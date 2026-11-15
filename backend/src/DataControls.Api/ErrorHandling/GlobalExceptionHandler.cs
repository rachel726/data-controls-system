using DataControls.Application.Ai;
using DataControls.Application.Common;
using DataControls.Application.Controls;
using DataControls.Core.Controls;
using DataControls.Core.Definitions;
using DataControls.Core.Sql;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.ErrorHandling;

/// <summary>
/// Maps every exception to an RFC 7807 ProblemDetails response with a Hebrew title/detail.
/// Known business errors keep their (user-facing, Hebrew) message; anything unexpected becomes a generic 500 -
/// internals never leak to the client, but the traceId lets support find the full error in the logs.
/// </summary>
public sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            InputValidationException e => new ValidationProblemDetails(e.Errors.ToDictionary(x => x.Key, x => x.Value))
            {
                Status = StatusCodes.Status400BadRequest, Title = "הנתונים שהוזנו אינם תקינים.", Detail = e.Message,
            },
            DefinitionInvalidException e => WithErrors(Problem(StatusCodes.Status400BadRequest, "הגדרת הבקרה אינה תקינה.", e.Message),
                [.. e.Errors.Select(ValidationErrorDto.From)]),
            DefinitionFormatException e => Problem(StatusCodes.Status400BadRequest, "מבנה הגדרת הבקרה אינו תקין.", e.Message),
            QueryOptionsException e => Problem(StatusCodes.Status400BadRequest, "בקשת התוצאות אינה תקינה.", e.Message),
            NotFoundException e => Problem(StatusCodes.Status404NotFound, "לא נמצא.", e.Message),
            DuplicateControlNameException e => Problem(StatusCodes.Status409Conflict, "שם הבקרה כבר קיים.", e.Message),
            QueryTimeoutException e => Problem(StatusCodes.Status504GatewayTimeout, "הרצת הבקרה ארכה זמן רב מדי.", e.Message),
            QueryExecutionException e => Problem(StatusCodes.Status500InternalServerError, "הרצת הבקרה נכשלה.", e.Message),
            AiUnavailableException e => Problem(StatusCodes.Status503ServiceUnavailable, "שירות ה-AI אינו זמין.", e.Message),
            AiResponseInvalidException e => Problem(StatusCodes.Status502BadGateway, "תשובת ה-AI אינה תקינה.", e.Message),
            // e.g. a body over the size limit (413) - the server's own status, with a Hebrew title
            BadHttpRequestException e => Problem(e.StatusCode,
                InvalidRequest.StatusTitles.GetValueOrDefault(e.StatusCode, "הבקשה אינה תקינה."), "הבקשה נדחתה. בדקו את הנתונים ונסו שוב."),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => null,
            _ => null,
        };

        if (problem is null)
        {
            if (httpContext.RequestAborted.IsCancellationRequested)
            {
                return true;   // the client went away - nothing to answer
            }

            LogUnhandled(logger, httpContext.Request.Method, httpContext.Request.Path, exception);
            problem = Problem(StatusCodes.Status500InternalServerError, "אירעה שגיאה בלתי צפויה.",
                "הפעולה לא הושלמה. נסו שוב, ואם התקלה חוזרת פנו לתמיכה עם מזהה התקלה.");
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail) => new() { Status = status, Title = title, Detail = detail };

    /// <summary>Validation errors of a definition, each pointing at a node ("filter.children[1].operand") so the UI can highlight it.</summary>
    private static ProblemDetails WithErrors(ProblemDetails problem, IReadOnlyList<ValidationErrorDto> errors)
    {
        problem.Extensions["definitionErrors"] = errors;
        return problem;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, string method, PathString path, Exception exception);
}
