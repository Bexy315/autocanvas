namespace AutoCanvas.Persistence.Identity;

/// <summary>
/// A personal API token for scripts and tools. It acts with its owner's current permissions; only
/// its SHA-256 hash is stored (ADR-0011).
/// </summary>
public sealed class ApiToken
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Column size of <see cref="Hint"/>.</summary>
    public const int MaxHintLength = 20;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Whose token this is.</summary>
    public required Guid UserId { get; init; }

    /// <summary>What the token is used for, chosen by its owner.</summary>
    public required string Name { get; init; }

    /// <summary>SHA-256 of the token, lower-case hex.</summary>
    public required string TokenHash { get; init; }

    /// <summary>The start and the last characters of the token, to recognize it in lists.</summary>
    public required string Hint { get; init; }

    /// <summary>When the token was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the token stops working; never if empty.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The last request made with the token (updated at most once a minute).</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Navigation to the user.</summary>
    public User? User { get; init; }
}
