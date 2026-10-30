namespace AutoCanvas.Persistence.Identity;

/// <summary>A local account. Users sign in with their user name (ADR-0011).</summary>
public sealed class User
{
    /// <summary>Column size of <see cref="UserName"/>.</summary>
    public const int MaxUserNameLength = 64;

    /// <summary>Column size of <see cref="DisplayName"/>.</summary>
    public const int MaxDisplayNameLength = 200;

    /// <summary>Column size of <see cref="Email"/>.</summary>
    public const int MaxEmailLength = 320;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The name used to sign in, in the spelling the user chose.</summary>
    public required string UserName { get; init; }

    /// <summary>Upper-case form of <see cref="UserName"/>; unique, so names differ in more than case.</summary>
    public required string NormalizedUserName { get; init; }

    /// <summary>The name shown to other people.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Contact address; information only, never verified or used to sign in.</summary>
    public string? Email { get; set; }

    /// <summary>Self-describing password hash (algorithm, parameters, salt, hash).</summary>
    public required string PasswordHash { get; set; }

    /// <summary>Instance administrator: manages users, groups and instance settings.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>A disabled user cannot sign in, and their tokens stop working.</summary>
    public bool IsDisabled { get; set; }

    /// <summary>Set when someone else chose the password; the user must replace it first.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>When the account was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the password was last set.</summary>
    public required DateTimeOffset PasswordChangedAt { get; set; }

    /// <summary>The last successful sign-in.</summary>
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>Groups the user belongs to.</summary>
    public ICollection<GroupMember> Memberships { get; } = [];
}
