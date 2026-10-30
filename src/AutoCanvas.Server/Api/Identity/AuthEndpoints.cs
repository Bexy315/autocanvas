using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Authentication;
using AutoCanvas.Server.Configuration;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Api.Identity;

/// <summary><c>/api/v1/auth</c>: signing in and out with the session cookie.</summary>
internal static class AuthEndpoints
{
    public const string SignInRateLimit = "sign-in";

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/sign-in", SignInAsync)
            .WithName("SignIn")
            .Produces<SignInResponse>()
            .WithSummary("Sign in")
            .WithDescription("Checks user name and password and sets the session cookie. Every failure gives the same answer. Too many failures for a name lock it for 15 minutes; too many attempts from one address are refused with 429.")
            .AllowAnonymous()
            .RequireRateLimiting(SignInRateLimit)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/sign-out", SignOutAsync)
            .WithName("SignOut")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Sign out")
            .WithDescription("Ends the current session and removes the cookie.")
            .RequireAuthorization(AutoCanvasPolicies.SignedIn);

        auth.MapPost("/sign-out-everywhere", SignOutEverywhereAsync)
            .WithName("SignOutEverywhere")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Sign out everywhere")
            .WithDescription("Ends every session of the signed-in user, on all devices.")
            .RequireAuthorization(AutoCanvasPolicies.SignedIn);
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        HttpContext context,
        SignInService signIn,
        IOptions<ServerOptions> server,
        CancellationToken cancellationToken)
    {
        var result = await signIn.SignInAsync(
            request.UserName,
            request.Password,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString(),
            cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            return ApiResults.Problem(error);
        }

        var signedIn = result.Value!;
        context.Response.Cookies.Append(AutoCanvasAuthenticationHandler.SessionCookie, signedIn.SessionToken, CookieOptions(context, server.Value, signedIn.ExpiresAt));
        return TypedResults.Ok(new SignInResponse(signedIn.Me, signedIn.ExpiresAt));
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, SessionService sessions, IOptions<ServerOptions> server, CancellationToken cancellationToken)
    {
        if (context.User.GetSessionId() is { } sessionId)
        {
            await sessions.EndAsync(context.User.GetUserId(), sessionId, cancellationToken).ConfigureAwait(false);
        }

        context.Response.Cookies.Delete(AutoCanvasAuthenticationHandler.SessionCookie, CookieOptions(context, server.Value, expires: null));
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SignOutEverywhereAsync(HttpContext context, SessionService sessions, IOptions<ServerOptions> server, CancellationToken cancellationToken)
    {
        await sessions.EndAllAsync(context.User.GetUserId(), cancellationToken).ConfigureAwait(false);
        context.Response.Cookies.Delete(AutoCanvasAuthenticationHandler.SessionCookie, CookieOptions(context, server.Value, expires: null));
        return TypedResults.NoContent();
    }

    /// <summary>
    /// <c>Secure</c> when the request came over HTTPS or the public URL is HTTPS (TLS ends at a
    /// proxy), so plain-HTTP test installations still work.
    /// </summary>
    private static CookieOptions CookieOptions(HttpContext context, ServerOptions server, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = context.Request.IsHttps || (server.PublicUrl?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?? false),
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}
