namespace AutoCanvas.Core.Model;

/// <summary>Starts runs on pushes to the repository the automation lives in.</summary>
/// <param name="Branches">Branch name patterns; empty means all branches.</param>
/// <param name="Tags">Tag name patterns; empty means tags do not trigger.</param>
/// <param name="Paths">File patterns of which a change must match one; empty means any change.</param>
public sealed record PushTrigger(
    IReadOnlyList<string> Branches,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Paths);
