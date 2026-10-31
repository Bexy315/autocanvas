using System.Text.Encodings.Web;
using AutoCanvas.Server.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Authentication;

/// <summary>
/// Authenticates requests by an API token (<c>Authorization: Bearer acp_…</c>) or, without one, by
/// the session cookie (ADR-0011). Requests without credentials stay anonymous; the authorization
/// result handler turns that into problem details. gRPC calls are left alone: they come from
/// runners, which authenticate with their own credential (ADR-0017).
/// </summary>
internal sealed class AutoCanvasAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions,
    TokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SessionCookie = "autocanvas_session";

    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.ContentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) == true)
        {
            return AuthenticateResult.NoResult();
        }

        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var owner = await tokens.ValidateAsync(authorization[BearerPrefix.Length..].Trim(), Context.RequestAborted).ConfigureAwait(false);
            return owner is null
                ? AuthenticateResult.Fail("The API token is unknown, expired or its owner is disabled.")
                : AuthenticateResult.Success(new AuthenticationTicket(AutoCanvasClaims.Create(owner, AutoCanvasClaims.TokenMethod), Scheme.Name));
        }

        if (!Request.Cookies.TryGetValue(SessionCookie, out var token) || string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var user = await sessions.ValidateAsync(token, Context.RequestAborted).ConfigureAwait(false);
        return user is null
            ? AuthenticateResult.Fail("The session has ended.")
            : AuthenticateResult.Success(new AuthenticationTicket(AutoCanvasClaims.Create(user, AutoCanvasClaims.SessionMethod), Scheme.Name));
    }
}
