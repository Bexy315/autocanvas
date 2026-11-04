namespace AutoCanvas.Server.Application.Repositories;

/// <summary>A branch or tag of a connected repository moved, as a poll or a webhook saw it.</summary>
/// <param name="Ref">The full ref, such as <c>refs/heads/main</c> or <c>refs/tags/v1.0</c>.</param>
/// <param name="Before">The commit it pointed to before; zeros for a new ref.</param>
/// <param name="After">The commit it points to now; zeros when it was deleted.</param>
internal sealed record PushEvent(string Ref, string Before, string After)
{
    /// <summary>The files the push changed; <see langword="null"/> when not known, then they are asked from the forge.</summary>
    public IReadOnlyList<string>? ChangedFiles { get; init; }

    /// <summary>Who pushed; the commit's author when not known.</summary>
    public string? Pusher { get; init; }

    /// <summary>The message of the pushed commit; its subject when not known.</summary>
    public string? Message { get; init; }

    /// <summary>Whether a commit is the all-zero one git uses for "none".</summary>
    public static bool IsNone(string commit) => commit.Length > 0 && commit.All(c => c == '0');
}
