namespace AutoCanvas.Forge;

/// <summary>The automation files of a repository at one commit.</summary>
/// <param name="Commit">The commit's full SHA.</param>
/// <param name="Subject">The first line of its message.</param>
/// <param name="Author">Its author's name.</param>
/// <param name="CommittedAt">When it was committed.</param>
/// <param name="Files">The files of <see cref="AutomationPaths.Directory"/> ending in <c>.yml</c> or <c>.yaml</c>.</param>
public sealed record RepositorySnapshot(string Commit, string Subject, string Author, DateTimeOffset CommittedAt, IReadOnlyList<RepositoryFile> Files);
