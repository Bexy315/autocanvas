namespace AutoCanvas.Persistence.Runners;

/// <summary>
/// A registered runner. It authenticates with a credential of which only the SHA-256 hash is
/// stored, and announces what it can do on every connect.
/// </summary>
public sealed class Runner
{
    /// <summary>Column size of <see cref="Name"/>.</summary>
    public const int MaxNameLength = 100;

    /// <summary>Column size of <see cref="CredentialHint"/>.</summary>
    public const int MaxHintLength = 20;

    /// <summary>Column size of <see cref="Capabilities"/>.</summary>
    public const int MaxCapabilitiesLength = 8000;

    /// <summary>Column size of <see cref="CreatedByName"/>.</summary>
    public const int MaxCreatedByLength = 100;

    /// <summary>Time-ordered identifier (UUID version 7); the protocol's <c>runner_id</c>.</summary>
    public required Guid Id { get; init; }

    /// <summary>Display name; unique regardless of case.</summary>
    public required string Name { get; set; }

    /// <summary>Upper-case form of <see cref="Name"/>.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>SHA-256 of the credential, lower-case hex.</summary>
    public required string CredentialHash { get; init; }

    /// <summary>The start and the last characters of the credential, to recognize it.</summary>
    public required string CredentialHint { get; init; }

    /// <summary>The capabilities last announced, as JSON.</summary>
    public required string Capabilities { get; set; }

    /// <summary>A paused runner stays connected and finishes its jobs but is offered no new ones.</summary>
    public bool IsPaused { get; set; }

    /// <summary>When the runner registered.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Who created the registration token it registered with.</summary>
    public required string CreatedByName { get; init; }

    /// <summary>The last call or heartbeat of the runner (updated at most every few seconds).</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>Raised with every change by an administrator; the API's ETag.</summary>
    public int Version { get; set; }
}
