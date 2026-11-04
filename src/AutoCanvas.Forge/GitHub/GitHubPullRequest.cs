namespace AutoCanvas.Forge.GitHub;

/// <summary>A pull request GitHub opened.</summary>
/// <param name="Number">Its number.</param>
/// <param name="HtmlUrl">Its page.</param>
public sealed record GitHubPullRequest(int Number, Uri HtmlUrl);
