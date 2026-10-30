namespace AutoCanvas.Server.Api.Identity;

/// <summary>A user to create. The user must change the password at the first sign-in.</summary>
/// <param name="UserName">The name to sign in with: letters, digits, dot, underscore, hyphen; unique regardless of case.</param>
/// <param name="Password">The initial password: 12 to 256 characters.</param>
/// <param name="DisplayName">The name shown to other people; defaults to the user name.</param>
/// <param name="Email">Contact address; information only.</param>
/// <param name="IsAdmin">Whether the user administers the instance.</param>
internal sealed record CreateUserRequest(string UserName, string Password, string? DisplayName = null, string? Email = null, bool IsAdmin = false);
