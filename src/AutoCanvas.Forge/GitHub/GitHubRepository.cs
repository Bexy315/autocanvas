namespace AutoCanvas.Forge.GitHub;

/// <summary>A repository on GitHub.</summary>
/// <param name="Id">GitHub's id for it, which stays when it is renamed or moved.</param>
/// <param name="FullName">Its <c>owner/name</c>.</param>
/// <param name="CloneUrl">Its clone address over https.</param>
/// <param name="DefaultBranch">Its default branch.</param>
/// <param name="Private">Whether it is private.</param>
public sealed record GitHubRepository(long Id, string FullName, Uri CloneUrl, string DefaultBranch, bool Private);
