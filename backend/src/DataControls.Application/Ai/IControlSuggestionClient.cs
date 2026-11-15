namespace DataControls.Application.Ai;

/// <summary>
/// The AI behind the "control from free text" feature. The application depends only on this - which service or
/// model answers is an infrastructure detail, so a model can be replaced without touching the controls logic.
/// </summary>
public interface IControlSuggestionClient
{
    /// <exception cref="AiUnavailableException">The AI could not be reached or did not answer in time.</exception>
    /// <exception cref="AiResponseInvalidException">The AI answered outside the agreed contract.</exception>
    Task<ControlSuggestion> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken);

    /// <summary>Requests the AI offers as one-click examples (ones it is known to handle).</summary>
    /// <exception cref="AiUnavailableException">The AI could not be reached or did not answer in time.</exception>
    Task<IReadOnlyList<AiExample>> GetExamplesAsync(CancellationToken cancellationToken);
}

/// <summary>The AI could not be reached or did not answer in time. Message is user-facing Hebrew.</summary>
public sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner)
{
    public const string DefaultMessage = "שירות ה-AI אינו זמין כרגע. נסו שוב בעוד מספר רגעים, או הגדירו את הבקרה ידנית.";
}

/// <summary>The AI answered, but not in the agreed format - the answer is not used. Message is user-facing Hebrew.</summary>
public sealed class AiResponseInvalidException(string message, Exception? inner = null) : Exception(message, inner)
{
    public const string DefaultMessage = "תשובת ה-AI אינה במבנה הצפוי ולכן לא ניתן להשתמש בה. נסו לנסח את הבקשה אחרת.";
}
