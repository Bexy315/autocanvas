namespace AutoCanvas.Persistence.Identity;

/// <summary>
/// A signed-in browser. Only the SHA-256 hash of the session token is stored, so a database copy
/// cannot be used to take over sessions (ADR-0011).
/// </summary>
public sealed class Session
{
    /// <summary>Length of <see cref="TokenHash"/>: SHA-256 as lower-case hex.</summary>
    public const int TokenHashLength = 64;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Whose session this is.</summary>
    public required Guid UserId { get; init; }

    /// <summary>SHA-256 of the token in the cookie, lower-case hex.</summary>
    public required string TokenHash { get; init; }

    /// <summary>When the user signed in.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last request with this session; it ends after a period without requests.</summary>
    public required DateTimeOffset LastSeenAt { get; set; }

    /// <summary>The session ends at this time at the latest, however active it is.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Network address the user signed in from.</summary>
    public string? ClientAddress { get; init; }

    /// <summary>Browser the user signed in with, shortened to the column size.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Navigation to the user.</summary>
    public User? User { get; init; }
}
