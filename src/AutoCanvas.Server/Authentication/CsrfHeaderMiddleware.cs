using AutoCanvas.Server.Api;

namespace AutoCanvas.Server.Authentication;

/// <summary>
/// Cross-site request forgery protection: state-changing API requests must carry
/// <c>X-AutoCanvas-Request: 1</c> unless they use a bearer token. A page on another site cannot set
/// such a header without a CORS preflight, which the server never allows. Sign-in is included, so a
/// foreign page cannot sign a browser into someone else's account.
/// </summary>
internal sealed class CsrfHeaderMiddleware(RequestDelegate next, IProblemDetailsService problems)
{
    public const string HeaderName = "X-AutoCanvas-Request";

    /// <summary>
    /// Webhook calls come from other systems with a token in the path, never with a session cookie,
    /// so there is no browser session a foreign page could ride on.
    /// </summary>
    public const string WebhookPath = "/api/v1/hooks";

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && !request.Path.StartsWithSegments(WebhookPath, StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method)
            && !request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Headers[HeaderName], "1", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails =
                {
                    Status = StatusCodes.Status403Forbidden,
                    Type = ProblemTypes.Forbidden,
                    Title = "Forbidden",
                    Detail = $"Requests that change something must send the header '{HeaderName}: 1' or an API token.",
                },
            }).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
