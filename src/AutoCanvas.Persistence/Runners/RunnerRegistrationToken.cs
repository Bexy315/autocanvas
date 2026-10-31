namespace AutoCanvas.Persistence.Runners;

/// <summary>
/// A one-time token an administrator hands to a runner so that it can register. Only its SHA-256
/// hash is stored; once used it stays as a record of which runner it registered.
/// </summary>
public sealed class RunnerRegistrationToken
{
    /// <summary>Column size of <see cref="Description"/>.</summary>
    public const int MaxDescriptionLength = 200;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>What the token is for, such as the machine it is meant for.</summary>
    public string? Description { get; init; }

    /// <summary>SHA-256 of the token, lower-case hex.</summary>
    public required string TokenHash { get; init; }

    /// <summary>The start and the last characters of the token, to recognize it.</summary>
    public required string Hint { get; init; }

    /// <summary>When the token was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The administrator who created it.</summary>
    public required string CreatedByName { get; init; }

    /// <summary>After this, the token cannot be used any more.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When a runner registered with it; a used token cannot be used again.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>The runner that registered with it, while that runner exists.</summary>
    public Guid? RunnerId { get; set; }
}
