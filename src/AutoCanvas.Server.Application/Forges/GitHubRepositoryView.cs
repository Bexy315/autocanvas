namespace AutoCanvas.Server.Application.Forges;

/// <summary>A repository the GitHub App can reach, to connect in a folder.</summary>
/// <param name="InstallationId">The installation it is reached through.</param>
/// <param name="Account">The account or organization of that installation.</param>
/// <param name="Id">GitHub's id of the repository.</param>
/// <param name="FullName">Its <c>owner/name</c>.</param>
/// <param name="DefaultBranch">Its default branch.</param>
/// <param name="Private">Whether it is private.</param>
public sealed record GitHubRepositoryView(long InstallationId, string Account, long Id, string FullName, string DefaultBranch, bool Private);
