namespace AutoCanvas.Server.Application.Identity;

/// <summary>An account to create.</summary>
/// <param name="UserName">The name to sign in with; unique regardless of case.</param>
/// <param name="Password">The initial password.</param>
public sealed record NewUser(string UserName, string Password)
{
    /// <summary>The name shown to other people; defaults to the user name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Contact address; information only.</summary>
    public string? Email { get; init; }

    /// <summary>Whether the user administers the instance.</summary>
    public bool IsAdmin { get; init; }

    /// <summary>
    /// Whether the user must replace the password on first sign-in: true whenever someone else
    /// chose it, which is every case except the command line with a password the admin typed.
    /// </summary>
    public bool MustChangePassword { get; init; } = true;
}
