namespace AutoCanvas.Server.Application.Identity;

/// <summary>A successful sign-in: the session token for the cookie, and who signed in.</summary>
/// <param name="SessionToken">The secret for the session cookie; stored only as a hash.</param>
/// <param name="ExpiresAt">When the session ends at the latest.</param>
/// <param name="Me">The signed-in user.</param>
public sealed record SignInResult(string SessionToken, DateTimeOffset ExpiresAt, MeView Me);
