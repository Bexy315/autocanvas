namespace AutoCanvas.Forge.GitHub;

/// <summary>A commit status, as GitHub shows it next to a commit and on pull requests.</summary>
/// <param name="State"><c>pending</c>, <c>success</c>, <c>failure</c> or <c>error</c>.</param>
/// <param name="Context">What it is about, such as <c>AutoCanvas / build (push)</c>; one status per context and commit.</param>
/// <param name="Description">A short sentence, cut at 140 characters.</param>
/// <param name="TargetUrl">Where it links to, if anywhere.</param>
public sealed record GitHubStatus(string State, string Context, string Description, Uri? TargetUrl);
