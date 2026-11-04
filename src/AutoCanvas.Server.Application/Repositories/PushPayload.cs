using System.Text.Json;
using AutoCanvas.Forge;

namespace AutoCanvas.Server.Application.Repositories;

/// <summary>
/// Reads the push events of forges' webhooks: GitHub's (which Gitea, Forgejo and Gogs send in the
/// same shape) and GitLab's. Both have <c>ref</c>, <c>before</c>, <c>after</c> and <c>commits</c>
/// with the files each added, modified and removed.
/// </summary>
internal static class PushPayload
{
    /// <summary>Above this many commits the list in the payload may be cut short, so the changed files count as not known.</summary>
    public const int MaxCommits = 20;

    /// <summary>The most changed files kept from a payload; with more, they count as not known.</summary>
    public const int MaxFiles = 1000;

    /// <summary>The events (from the forge's event header) that are pushes.</summary>
    private static readonly HashSet<string> PushEvents = new(StringComparer.OrdinalIgnoreCase) { "push", "Push Hook", "Tag Push Hook" };

    /// <summary>Whether an event named by the forge is a push; no name means the body decides.</summary>
    public static bool IsPush(string? eventName) => eventName is null || PushEvents.Contains(eventName);

    /// <summary>The push in a payload, or <see langword="null"/> when it is not one.</summary>
    public static PushEvent? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || (Text(root, "object_kind") is { } kind && kind is not ("push" or "tag_push"))
            || Text(root, "ref") is not { } gitRef || !GenericGitForge.IsFullRef(gitRef)
            || Text(root, "before") is not { } before || !IsCommit(before)
            || Text(root, "after") is not { } after || !IsCommit(after))
        {
            return null;
        }

        var commits = root.TryGetProperty("commits", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
        return new PushEvent(gitRef, before, after)
        {
            ChangedFiles = Changes(root, before, commits),
            Pusher = Pusher(root),
            Message = (root.TryGetProperty("head_commit", out var head) && head.ValueKind == JsonValueKind.Object ? Text(head, "message") : null)
                ?? commits.Where(c => string.Equals(Text(c, "id"), after, StringComparison.Ordinal)).Select(c => Text(c, "message")).FirstOrDefault(),
        };
    }

    /// <summary>
    /// The files the push changed, from its commits; not known for a new ref, a forced push, no
    /// commits or a list that may be cut short.
    /// </summary>
    private static List<string>? Changes(JsonElement root, string before, List<JsonElement> commits)
    {
        var total = root.TryGetProperty("total_commits_count", out var count) && count.TryGetInt32(out var value) ? value : commits.Count;
        if (PushEvent.IsNone(before) || (root.TryGetProperty("forced", out var forced) && forced.ValueKind == JsonValueKind.True)
            || commits.Count == 0 || commits.Count >= MaxCommits || total > commits.Count)
        {
            return null;
        }

        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var commit in commits)
        {
            foreach (var kind in (string[])["added", "modified", "removed"])
            {
                if (commit.TryGetProperty(kind, out var paths) && paths.ValueKind == JsonValueKind.Array)
                {
                    files.UnionWith(paths.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!));
                }
            }
        }

        return files.Count > MaxFiles ? null : [.. files];
    }

    private static string? Pusher(JsonElement root)
    {
        if (root.TryGetProperty("pusher", out var pusher) && pusher.ValueKind == JsonValueKind.Object)
        {
            return Text(pusher, "name") ?? Text(pusher, "username") ?? Text(pusher, "login");
        }

        return Text(root, "user_username") ?? Text(root, "user_name");
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static bool IsCommit(string value) => value.Length is 40 or 64 && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
