namespace AutoCanvas.Persistence.Repositories;

/// <summary>
/// A push a forge reported through a webhook, waiting to be handled by the server in the
/// background, so that the forge gets its answer at once. Handling takes the row out.
/// </summary>
public sealed class RepositoryPush
{
    /// <summary>Column size of <see cref="Pusher"/>.</summary>
    public const int MaxPusherLength = 200;

    /// <summary>Column size of <see cref="Message"/>.</summary>
    public const int MaxMessageLength = 2000;

    /// <summary>Time-ordered identifier (UUID version 7).</summary>
    public required Guid Id { get; init; }

    /// <summary>The repository.</summary>
    public required Guid RepositoryId { get; init; }

    /// <summary>The full ref, such as <c>refs/heads/main</c>.</summary>
    public required string Ref { get; init; }

    /// <summary>The commit the ref pointed to before; zeros for a new ref.</summary>
    public required string Before { get; init; }

    /// <summary>The commit it points to now; zeros when it was deleted.</summary>
    public required string After { get; init; }

    /// <summary>The changed files as a JSON array; <see langword="null"/> when the forge did not say.</summary>
    public string? ChangedFiles { get; init; }

    /// <summary>Who pushed, as the forge said.</summary>
    public string? Pusher { get; init; }

    /// <summary>The message of the pushed commit, as the forge said.</summary>
    public string? Message { get; init; }

    /// <summary>When the webhook call arrived.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}
