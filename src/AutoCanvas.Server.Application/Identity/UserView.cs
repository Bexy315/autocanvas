using AutoCanvas.Persistence.Identity;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>A user as administrators and the user themselves see it; never the password hash.</summary>
/// <param name="Id">The user's id.</param>
/// <param name="UserName">The name used to sign in.</param>
/// <param name="DisplayName">The name shown to other people.</param>
/// <param name="Email">Contact address, if given.</param>
/// <param name="IsAdmin">Whether the user administers the instance.</param>
/// <param name="IsDisabled">Whether the user is blocked from signing in.</param>
/// <param name="MustChangePassword">Whether the user has to replace a password someone else set.</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="LastSignInAt">The last successful sign-in, if any.</param>
public sealed record UserView(
    Guid Id,
    string UserName,
    string DisplayName,
    string? Email,
    bool IsAdmin,
    bool IsDisabled,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt)
{
    internal static UserView From(User user) => new(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.Email,
        user.IsAdmin,
        user.IsDisabled,
        user.MustChangePassword,
        user.CreatedAt,
        user.LastSignInAt);
}
