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
    public async Task<RepositorySnapshot> ReadAutomationsAsync(RepositoryAccess access, string branch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        Check(access);
        CheckBranch(branch);
        var cache = access.CacheDirectory;
        if (!Directory.Exists(Path.Combine(cache, "objects")))
        {
            Directory.CreateDirectory(cache);
            await git.RunAsync(["init", "--bare", "--quiet", cache], access: null, cancellationToken).ConfigureAwait(false);
        }

        await git.RunAsync(["-C", cache, "fetch", "--depth", "1", "--no-tags", "--quiet", "--", access.Url.ToString(), $"refs/heads/{branch}"], access, cancellationToken).ConfigureAwait(false);
        var commit = (await git.RunAsync(["-C", cache, "rev-parse", "FETCH_HEAD"], access: null, cancellationToken).ConfigureAwait(false)).Trim();
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

    [GeneratedRegex(@"^(?<sha>[0-9a-f]{40}|[0-9a-f]{64})\tHEAD$", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Head();

    /// <summary>A safe subset of git's branch names: no leading dash or dot, no "..", no spaces or control characters.</summary>
    [GeneratedRegex(@"^(?![-.])(?!.*\.\.)[A-Za-z0-9._/+-]{1,200}(?<![./])$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BranchName();
}
