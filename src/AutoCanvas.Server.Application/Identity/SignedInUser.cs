namespace AutoCanvas.Server.Application.Identity;

/// <summary>Who a valid session or token belongs to, as the host needs it for one request.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="UserName">The name used to sign in.</param>
/// <param name="DisplayName">The name shown to other people.</param>
/// <param name="IsAdmin">Whether the user administers the instance.</param>
/// <param name="MustChangePassword">Whether only the password change is allowed right now.</param>
/// <param name="SessionId">The session, when the request uses one.</param>
public sealed record SignedInUser(Guid UserId, string UserName, string DisplayName, bool IsAdmin, bool MustChangePassword, Guid? SessionId);
