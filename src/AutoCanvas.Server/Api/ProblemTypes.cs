using System.Diagnostics;

namespace AutoCanvas.Server.Api;

/// <summary>
/// Stable <c>type</c> URIs of the problem details the API returns. Each points to its section in
/// the API conventions page, so the anchors there must not change.
/// </summary>
internal static class ProblemTypes
{
    private const string Base = "https://bexy315.github.io/autocanvas/technical/api#";

    public const string Validation = Base + "validation";
    public const string Unauthorized = Base + "unauthorized";
    public const string Forbidden = Base + "forbidden";
    public const string NotFound = Base + "not-found";
    public const string Conflict = Base + "conflict";
    public const string PreconditionFailed = Base + "precondition-failed";
    public const string PasswordChangeRequired = Base + "password-change-required";
    public const string TooManyRequests = Base + "too-many-requests";

    /// <summary>The installation is in setup mode; only the setup answers (ADR-0020).</summary>
    public const string SetupRequired = Base + "setup-required";

    /// <summary>The request body is larger than the endpoint takes.</summary>
    public const string TooLarge = Base + "too-large";
    public const string Internal = Base + "internal";

    /// <summary>The type for a status code, or <see langword="null"/> when the API defines none.</summary>
    public static string? ForStatus(int? status) => status switch
    {
        StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity => Validation,
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status412PreconditionFailed => PreconditionFailed,
        StatusCodes.Status429TooManyRequests => TooManyRequests,
        >= StatusCodes.Status500InternalServerError => Internal,
        _ => null,
    };

    /// <summary>
    /// Every problem the server writes gets the API's own type (unless the endpoint set a specific
    /// one) and a trace id that matches the server log.
    /// </summary>
    public static IServiceCollection AddAutoCanvasProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            if (problem.Type is null || !problem.Type.StartsWith(Base, StringComparison.Ordinal))
            {
                problem.Type = ForStatus(problem.Status) ?? problem.Type;
            }

            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });
}
