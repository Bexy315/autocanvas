namespace AutoCanvas.Forge;

/// <summary>A branch and the commit it points to.</summary>
/// <param name="Branch">The branch's name, without <c>refs/heads/</c>.</param>
/// <param name="Commit">The commit's full SHA.</param>
public sealed record ResolvedBranch(string Branch, string Commit);
