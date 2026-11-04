using AutoCanvas.Core.Matching;

namespace AutoCanvas.Core.Model;

/// <summary>Starts runs on pushes to the repository the automation lives in.</summary>
/// <param name="Branches">Branch name patterns; empty means all branches.</param>
/// <param name="Tags">Tag name patterns; empty means tags do not trigger.</param>
/// <param name="Paths">File patterns of which a change must match one; empty means any change.</param>
public sealed record PushTrigger(
    IReadOnlyList<string> Branches,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Paths)
{
    /// <summary>The prefix of branch refs.</summary>
    public const string BranchPrefix = "refs/heads/";

    /// <summary>The prefix of tag refs.</summary>
    public const string TagPrefix = "refs/tags/";

    /// <summary>
    /// Whether a push to <paramref name="gitRef"/> (such as <c>refs/heads/main</c> or
    /// <c>refs/tags/v1.0</c>) starts a run (<see cref="Glob"/> patterns). The paths are checked only
    /// when the changed files are known: when they are not (<see langword="null"/>), any change counts.
    /// </summary>
    public bool Matches(string gitRef, IReadOnlyCollection<string>? changedFiles)
    {
        ArgumentNullException.ThrowIfNull(gitRef);
        var refMatches = gitRef.StartsWith(BranchPrefix, StringComparison.Ordinal)
            ? Branches.Count == 0 || Glob.MatchesAny(Branches, gitRef[BranchPrefix.Length..])
            : gitRef.StartsWith(TagPrefix, StringComparison.Ordinal) && Glob.MatchesAny(Tags, gitRef[TagPrefix.Length..]);
        return refMatches && (Paths.Count == 0 || changedFiles is null || changedFiles.Any(f => Glob.MatchesAny(Paths, f)));
    }
}
