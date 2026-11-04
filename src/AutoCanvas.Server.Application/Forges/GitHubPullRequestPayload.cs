using System.Text.Json;
using AutoCanvas.Persistence.Repositories;

namespace AutoCanvas.Server.Application.Forges;

/// <summary>What AutoCanvas reads from GitHub's <c>pull_request</c> event.</summary>
/// <param name="RepositoryId">GitHub's id of the repository the pull request goes into.</param>
/// <param name="Number">The pull request's number.</param>
/// <param name="Action">What happened.</param>
/// <param name="HeadSha">The commit at its head.</param>
/// <param name="HeadBranch">The branch it comes from.</param>
/// <param name="BaseBranch">The branch it goes into.</param>
/// <param name="Title">Its title.</param>
/// <param name="Author">Who opened it.</param>
/// <param name="Url">Its page.</param>
/// <param name="Fork">Whether its head is in another repository (or one that is gone).</param>
internal sealed record GitHubPullRequestPayload(
    long RepositoryId,
    int Number,
    string Action,
    string HeadSha,
    string HeadBranch,
    string BaseBranch,
    string Title,
    string Author,
    Uri? Url,
    bool Fork)
{
    /// <summary>The event, or <see langword="null"/> when it lacks what a run needs.</summary>
    public static GitHubPullRequestPayload? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || Text(root, "action") is not { } action
            || !JsonFields.Object(root, "pull_request", out var pull)
            || !JsonFields.Int32(pull, "number", out var number)
            || !JsonFields.Object(pull, "head", out var head) || !JsonFields.Object(pull, "base", out var @base)
            || Text(head, "sha") is not { } sha || !IsCommit(sha)
            || Text(head, "ref") is not { } headBranch || Text(@base, "ref") is not { } baseBranch
            || !JsonFields.Object(@base, "repo", out var baseRepo) || !JsonFields.Int64(baseRepo, "id", out var baseId))
        {
            return null;
        }

        // A fork's head lives in another repository; one whose fork was deleted has none.
        var fork = !JsonFields.Object(head, "repo", out var headRepo) || !JsonFields.Int64(headRepo, "id", out var headRepositoryId) || headRepositoryId != baseId;
        return new GitHubPullRequestPayload(
            baseId,
            number,
            action,
            sha,
            headBranch,
            baseBranch,
            Text(pull, "title") ?? string.Empty,
            JsonFields.Object(pull, "user", out var user) ? Text(user, "login") ?? "github" : "github",
            Text(pull, "html_url") is { } url && Uri.TryCreate(url, UriKind.Absolute, out var page) ? page : null,
            fork);
    }

    /// <summary>The event as a row of the queue.</summary>
    public RepositoryPullRequest ToRow(Guid id, Guid repositoryId, DateTimeOffset receivedAt) => new()
    {
        Id = id,
        RepositoryId = repositoryId,
        Number = Number,
        Action = Cut(Action, RepositoryPullRequest.MaxActionLength),
        HeadSha = HeadSha,
        HeadBranch = Cut(HeadBranch, RepositoryRef.MaxRefLength),
        BaseBranch = Cut(BaseBranch, RepositoryRef.MaxRefLength),
        Title = Cut(Title, RepositoryPullRequest.MaxTitleLength),
        Author = Cut(Author, RepositoryPush.MaxPusherLength),
        Url = Url is { } url && url.OriginalString.Length <= Repository.MaxUrlLength ? url : null,
        Fork = Fork,
        ReceivedAt = receivedAt,
    };

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static bool IsCommit(string value) => value.Length is 40 or 64 && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
