using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoCanvas.Forge;

/// <summary>
/// Any Git repository over HTTP(S) (ADR-0021), read with the <c>git</c> command line: the branch's
/// commit with <c>ls-remote</c>, the files from a shallow fetch into a bare copy in the cache
/// directory. It has no statuses, comments or pull requests.
/// </summary>
public sealed partial class GenericGitForge(TimeSpan timeout) : IForge
{
    private readonly GitCli git = new(timeout);

    /// <inheritdoc />
    public ForgeKind Kind => ForgeKind.GenericGit;

    /// <inheritdoc />
    public ForgeCapabilities Capabilities => ForgeCapabilities.None;

    /// <inheritdoc />
    public async Task<ResolvedBranch> ResolveAsync(RepositoryAccess access, string? branch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        Check(access);
        if (branch is null)
        {
            var head = await git.RunAsync(["ls-remote", "--symref", "--", access.Url.ToString(), "HEAD"], access, cancellationToken).ConfigureAwait(false);
            var target = SymbolicHead().Match(head);
            var commit = Head().Match(head);
            if (!target.Success || !commit.Success)
            {
                throw new ForgeException($"The repository {access.Url} has no default branch yet; push a first commit, or name the branch.");
            }

            return new ResolvedBranch(target.Groups["branch"].Value, commit.Groups["sha"].Value);
        }

        CheckBranch(branch);
        var refs = await git.RunAsync(["ls-remote", "--", access.Url.ToString(), $"refs/heads/{branch}"], access, cancellationToken).ConfigureAwait(false);
        var line = refs.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return line is null
            ? throw new ForgeException($"The branch '{branch}' does not exist in {access.Url}.")
            : new ResolvedBranch(branch, line.Split('\t')[0].Trim());
    }

    /// <inheritdoc />
    public async Task<RepositorySnapshot> ReadAutomationsAsync(RepositoryAccess access, string gitRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        Check(access);
        var fullRef = FullRef(gitRef);
        var cache = await CacheAsync(access, cancellationToken).ConfigureAwait(false);
        await git.RunAsync(["-C", cache, "fetch", "--depth", "1", "--no-tags", "--quiet", "--", access.Url.ToString(), fullRef], access, cancellationToken).ConfigureAwait(false);
        var commit = (await git.RunAsync(["-C", cache, "rev-parse", "FETCH_HEAD^{commit}"], access: null, cancellationToken).ConfigureAwait(false)).Trim();
        var meta = (await git.RunAsync(["-C", cache, "log", "-1", "--format=%s%x00%an%x00%cI", commit], access: null, cancellationToken).ConfigureAwait(false)).TrimEnd('\n').Split('\0');
        var tree = await git.RunAsync(["-C", cache, "ls-tree", "-l", "--full-tree", commit, "--", AutomationPaths.Directory + "/"], access: null, cancellationToken).ConfigureAwait(false);

        var files = new List<RepositoryFile>();
        foreach (var entry in tree.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<mode> <type> <object> <size>\t<path>"
            var parts = entry.Split('\t', 2);
            var fields = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var path = parts[1];
            if (fields.Length < 4 || !string.Equals(fields[1], "blob", StringComparison.Ordinal) || !AutomationPaths.IsAutomation(path))
            {
                continue;
            }

            if (files.Count == AutomationPaths.MaxFiles)
            {
                throw new ForgeException($"The repository has more than {AutomationPaths.MaxFiles} automation files in {AutomationPaths.Directory}.");
            }

            if (long.Parse(fields[3], CultureInfo.InvariantCulture) > AutomationPaths.MaxFileBytes)
            {
                throw new ForgeException($"{path} is larger than {AutomationPaths.MaxFileBytes / 1024} KiB.");
            }

            files.Add(new RepositoryFile(path, await git.RunAsync(["-C", cache, "cat-file", "blob", fields[2]], access: null, cancellationToken).ConfigureAwait(false)));
        }

        return new RepositorySnapshot(
            commit,
            meta.ElementAtOrDefault(0) ?? string.Empty,
            meta.ElementAtOrDefault(1) ?? string.Empty,
            DateTimeOffset.Parse(meta.ElementAtOrDefault(2) ?? string.Empty, CultureInfo.InvariantCulture),
            files);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ListRefsAsync(RepositoryAccess access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        Check(access);
        var listed = await git.RunAsync(["ls-remote", "--", access.Url.ToString()], access, cancellationToken).ConfigureAwait(false);
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        var peeled = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in listed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<sha>\t<ref>"; an annotated tag comes twice, the second time as "<ref>^{}" with its commit.
            var parts = line.Trim().Split('\t', 2);
            if (parts.Length != 2 || !Sha().IsMatch(parts[0]))
            {
                continue;
            }

            var name = parts[1];
            if (name.EndsWith("^{}", StringComparison.Ordinal))
            {
                peeled[name[..^3]] = parts[0];
            }
            else if (IsFullRef(name))
            {
                refs[name] = parts[0];
            }
        }

        foreach (var (name, commit) in peeled)
        {
            if (refs.ContainsKey(name))
            {
                refs[name] = commit;
            }
        }

        return refs;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> ChangedFilesAsync(RepositoryAccess access, string gitRef, string before, string after, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        Check(access);
        var fullRef = FullRef(gitRef);
        if (!Sha().IsMatch(before) || !Sha().IsMatch(after) || before.All(c => c == '0'))
        {
            return null;
        }

        var cache = await CacheAsync(access, cancellationToken).ConfigureAwait(false);
        await git.RunAsync(["-C", cache, "fetch", "--depth", ChangeDepth, "--no-tags", "--quiet", "--", access.Url.ToString(), fullRef], access, cancellationToken).ConfigureAwait(false);
        try
        {
            await git.RunAsync(["-C", cache, "cat-file", "-e", before + "^{commit}"], access: null, cancellationToken).ConfigureAwait(false);
            await git.RunAsync(["-C", cache, "cat-file", "-e", after + "^{commit}"], access: null, cancellationToken).ConfigureAwait(false);
        }
        catch (ForgeException)
        {
            // Further back than the fetch reaches, or on another line of history (a forced push).
            return null;
        }

        var diff = await git.RunAsync(["-C", cache, "diff", "--name-only", "--no-renames", "-z", before, after], access: null, cancellationToken).ConfigureAwait(false);
        return diff.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Whether a name is a full ref of a branch or a tag with a name of the safe subset.</summary>
    public static bool IsFullRef(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return (name.StartsWith(BranchPrefix, StringComparison.Ordinal) && BranchName().IsMatch(name[BranchPrefix.Length..]))
            || (name.StartsWith(TagPrefix, StringComparison.Ordinal) && BranchName().IsMatch(name[TagPrefix.Length..]));
    }

    /// <summary>How many commits back a change is looked for.</summary>
    private const string ChangeDepth = "50";

    private const string BranchPrefix = "refs/heads/";

    private const string TagPrefix = "refs/tags/";

    /// <summary>A branch's name or a full ref, as a full ref.</summary>
    private static string FullRef(string gitRef)
    {
        ArgumentNullException.ThrowIfNull(gitRef);
        if (gitRef.StartsWith("refs/", StringComparison.Ordinal))
        {
            return IsFullRef(gitRef) || PullHead().IsMatch(gitRef) ? gitRef : throw new ForgeException($"'{gitRef}' is not a branch, tag or pull request.");
        }

        CheckBranch(gitRef);
        return BranchPrefix + gitRef;
    }

    /// <summary>The bare copy of the repository in the cache, created when there is none yet.</summary>
    private async Task<string> CacheAsync(RepositoryAccess access, CancellationToken cancellationToken)
    {
        var cache = access.CacheDirectory;
        if (!Directory.Exists(Path.Combine(cache, "objects")))
        {
            Directory.CreateDirectory(cache);
            await git.RunAsync(["init", "--bare", "--quiet", cache], access: null, cancellationToken).ConfigureAwait(false);
        }

        return cache;
    }

    private static void Check(RepositoryAccess access)
    {
        if (!string.Equals(access.Url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && !string.Equals(access.Url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
        {
            throw new ForgeException("A Git repository is reached over http or https.");
        }
    }

    private static void CheckBranch(string branch)
    {
        if (!BranchName().IsMatch(branch))
        {
            throw new ForgeException($"'{branch}' is not a branch name.");
        }
    }

    [GeneratedRegex(@"^ref: refs/heads/(?<branch>\S+)\tHEAD$", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SymbolicHead();

    [GeneratedRegex(@"^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Sha();

    /// <summary>The head of a pull request, as forges keep it in the repository it goes into.</summary>
    [GeneratedRegex(@"^refs/pull/[0-9]{1,9}/head$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PullHead();

    [GeneratedRegex(@"^(?<sha>[0-9a-f]{40}|[0-9a-f]{64})\tHEAD$", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Head();

    /// <summary>A safe subset of git's branch names: no leading dash or dot, no "..", no spaces or control characters.</summary>
    [GeneratedRegex(@"^(?![-.])(?!.*\.\.)[A-Za-z0-9._/+-]{1,200}(?<![./])$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BranchName();
}
