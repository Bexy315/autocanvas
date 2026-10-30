using AutoCanvas.Server.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace AutoCanvas.Server.Authentication;

/// <summary>
/// Answers refused requests with problem details: 401 when nobody is signed in, 403 with
/// <see cref="ProblemTypes.PasswordChangeRequired"/> when only the password change is allowed,
/// 403 otherwise.
/// </summary>
internal sealed class ProblemAuthorizationResultHandler(IProblemDetailsService problems) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Challenged)
        {
            return WriteAsync(context, StatusCodes.Status401Unauthorized, ProblemTypes.Unauthorized, "Unauthorized", "Sign in, or send a valid API token.");
        }

        if (authorizeResult.Forbidden)
        {
            var passwordChange = authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<PasswordChangedRequirement>().Any() == true;
            return passwordChange
                ? WriteAsync(context, StatusCodes.Status403Forbidden, ProblemTypes.PasswordChangeRequired, "Password change required", "Change your password first (PUT /api/v1/me/password).")
                : WriteAsync(context, StatusCodes.Status403Forbidden, ProblemTypes.Forbidden, "Forbidden", "You may not do this.");
        }

        return fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    private async Task WriteAsync(HttpContext context, int status, string type, string title, string detail)
    {
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Status = status, Type = type, Title = title, Detail = detail },
        }).ConfigureAwait(false);
    }
}
