namespace AutoCanvas.Server.Application.Forges;

/// <summary>Whether GitHub is set up, and how.</summary>
/// <param name="App">The GitHub App; <see langword="null"/> while none is set up.</param>
public sealed record GitHubForgeView(GitHubAppView? App);
