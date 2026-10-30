using AutoCanvas.Server.Application.Common;

namespace AutoCanvas.Server.Api;

/// <summary>Turns use case outcomes into HTTP results; refusals become problem details.</summary>
internal static class ApiResults
{
    public static IResult Ok<T>(UseCaseResult<T> result) => result.Error is { } error ? Problem(error) : TypedResults.Ok(result.Value);

    public static IResult NoContent<T>(UseCaseResult<T> result) => result.Error is { } error ? Problem(error) : TypedResults.NoContent();

    public static IResult Created<T>(UseCaseResult<T> result, Func<T, string> location) =>
        result.Error is { } error ? Problem(error) : TypedResults.Created(new Uri(location(result.Value!), UriKind.Relative), result.Value);

    public static IResult Problem(UseCaseError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var (status, type, title) = error.Kind switch
        {
            UseCaseErrorKind.Validation => (StatusCodes.Status400BadRequest, ProblemTypes.Validation, "Invalid request"),
            UseCaseErrorKind.NotFound => (StatusCodes.Status404NotFound, ProblemTypes.NotFound, "Not found"),
            UseCaseErrorKind.Conflict => (StatusCodes.Status409Conflict, ProblemTypes.Conflict, "Conflict"),
            UseCaseErrorKind.Forbidden => (StatusCodes.Status403Forbidden, ProblemTypes.Forbidden, "Forbidden"),
            UseCaseErrorKind.Unauthorized => (StatusCodes.Status401Unauthorized, ProblemTypes.Unauthorized, "Unauthorized"),
            UseCaseErrorKind.TooManyRequests => (StatusCodes.Status429TooManyRequests, ProblemTypes.TooManyRequests, "Too many requests"),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Unknown error kind."),
        };

        var extensions = error.Field is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal) { ["field"] = error.Field };
        return TypedResults.Problem(error.Message, statusCode: status, title: title, type: type, extensions: extensions);
    }
}
