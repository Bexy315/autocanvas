using AutoCanvas.Core.Matching;

namespace AutoCanvas.Core.Model;

/// <summary>Starts runs on pull requests in the repository the automation lives in.</summary>
/// <param name="Branches">Patterns of the branch the pull request goes into; empty means all.</param>
/// <param name="Paths">File patterns of which a change of the pull request must match one; empty means any change.</param>
/// <param name="Types">What happened to the pull request; <see cref="DefaultTypes"/> when not given.</param>
public sealed record PullRequestTrigger(
    IReadOnlyList<string> Branches,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> Types)
{
    /// <summary>What starts runs when <c>types</c> is not given: a new pull request, new commits, and one reopened.</summary>
    public static readonly IReadOnlyList<string> DefaultTypes = ["opened", "synchronize", "reopened"];

    /// <summary>Every type a trigger may name.</summary>
    public static readonly IReadOnlyList<string> KnownTypes = ["opened", "synchronize", "reopened", "ready_for_review", "edited"];

    /// <summary>
    /// Whether a pull request into <paramref name="baseBranch"/> that saw <paramref name="action"/>
    /// starts a run (<see cref="Glob"/> patterns). The paths are checked only when the changed files
    /// are known: when they are not (<see langword="null"/>), any change counts.
    /// </summary>
    public bool Matches(string action, string baseBranch, IReadOnlyCollection<string>? changedFiles)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(baseBranch);
        return Types.Contains(action, StringComparer.Ordinal)
            && (Branches.Count == 0 || Glob.MatchesAny(Branches, baseBranch))
            && (Paths.Count == 0 || changedFiles is null || changedFiles.Any(f => Glob.MatchesAny(Paths, f)));
    }
}
