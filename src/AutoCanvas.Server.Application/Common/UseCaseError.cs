namespace AutoCanvas.Server.Application.Common;

/// <summary>A refused request: what kind of refusal, a message for people, and the field at fault.</summary>
/// <param name="Kind">Decides the HTTP status and problem type.</param>
/// <param name="Message">Says what is wrong and how to fix it; shown to users.</param>
/// <param name="Field">The request field the message is about, if any (camelCase as in the API).</param>
public sealed record UseCaseError(UseCaseErrorKind Kind, string Message, string? Field = null)
{
    /// <summary>The input breaks a rule.</summary>
    public static UseCaseError Invalid(string field, string message) => new(UseCaseErrorKind.Validation, message, field);

    /// <summary>The object does not exist.</summary>
    public static UseCaseError NotFound(string message) => new(UseCaseErrorKind.NotFound, message);

    /// <summary>The request clashes with the current state.</summary>
    public static UseCaseError Conflict(string message, string? field = null) => new(UseCaseErrorKind.Conflict, message, field);
}
