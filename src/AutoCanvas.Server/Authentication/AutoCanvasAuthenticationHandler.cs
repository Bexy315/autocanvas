using System.Text.Encodings.Web;
using AutoCanvas.Server.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Authentication;

/// <summary>
/// Authenticates requests by the session cookie (ADR-0011). Requests without credentials stay
/// anonymous; the authorization result handler turns that into problem details.
/// </summary>
internal sealed class AutoCanvasAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SessionCookie = "autocanvas_session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
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
