using System.Globalization;
using System.Security.Claims;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;

namespace AutoCanvas.Server.Authentication;

/// <summary>The claims the AutoCanvas scheme issues, and reading them back.</summary>
internal static class AutoCanvasClaims
{
    public const string Scheme = "AutoCanvas";
    public const string UserId = "autocanvas:user_id";
    public const string UserName = "autocanvas:user_name";
    public const string DisplayName = "autocanvas:display_name";
    public const string Admin = "autocanvas:admin";
    public const string MustChangePassword = "autocanvas:must_change_password";
    public const string Method = "autocanvas:method";
    public const string SessionId = "autocanvas:session_id";

    public const string SessionMethod = "session";
    public const string TokenMethod = "token";

    public static ClaimsPrincipal Create(SignedInUser user, string method)
    {
        var claims = new List<Claim>
        {
            new(UserId, user.UserId.ToString()),
            new(UserName, user.UserName),
            new(DisplayName, user.DisplayName),
            new(Method, method),
        };
        if (user.IsAdmin)
        {
            claims.Add(new Claim(Admin, "true"));
        }

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(MustChangePassword, "true"));
        }

        if (user.SessionId is { } sessionId)
        {
            claims.Add(new Claim(SessionId, sessionId.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, UserName, null));
    }

    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(UserId) ?? throw new InvalidOperationException("The request is not authenticated."), CultureInfo.InvariantCulture);

    public static Guid? GetSessionId(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(SessionId) is { } id ? Guid.Parse(id, CultureInfo.InvariantCulture) : null;

    /// <summary>The signed-in user as actor of folder use cases.</summary>
    public static Actor ToActor(this ClaimsPrincipal principal) =>
        new(principal.GetUserId(), principal.HasClaim(Admin, "true"));

    public static bool UsesSession(this ClaimsPrincipal principal) =>
        string.Equals(principal.FindFirstValue(Method), SessionMethod, StringComparison.Ordinal);
}
