namespace AutoCanvas.Runner.Execution;

/// <summary>
/// Where a job's files are: on the runner's side, on the Docker host's side (for bind mounts), and in
/// the job's container. The workspace is <c>/workspace</c>; the runner's own files for the job
/// (step scripts, output files) are in <c>/autocanvas</c>, outside the workspace, so that steps and
/// reports never see them.
/// </summary>
internal sealed class JobPaths(RunnerOptions options, string jobId)
{
    public const string ContainerWorkspace = "/workspace";

    public const string ContainerMeta = "/autocanvas";

    /// <summary>The job's directory on the runner's side.</summary>
    public string Root { get; } = Path.GetFullPath(Path.Combine(options.WorkRoot, jobId));

    public string Workspace => Path.Combine(Root, "workspace");

    public string Meta => Path.Combine(Root, "meta");

    /// <summary>The bind mounts of the job's container.</summary>
    public IReadOnlyList<string> Binds => [$"{HostPath("workspace")}:{ContainerWorkspace}", $"{HostPath("meta")}:{ContainerMeta}"];

    /// <summary>
    /// A directory relative to the workspace, as the container sees it; <see langword="null"/> when
    /// the path is absolute or leads out of the workspace.
    /// </summary>
    public static string? InContainer(string? relative) =>
        Normalize(relative) is { } parts ? parts.Count == 0 ? ContainerWorkspace : $"{ContainerWorkspace}/{string.Join('/', parts)}" : null;

    /// <summary>The same directory on the runner's side, to create it before a step runs there.</summary>
    public string? OnRunner(string? relative) =>
        Normalize(relative) is { } parts ? Path.Combine([Workspace, .. parts]) : null;

    /// <summary>The path segments of a relative path, without <c>.</c> and resolved <c>..</c>.</summary>
    private static List<string>? Normalize(string? relative)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(relative))
        {
            return parts;
        }

        if (relative[0] is '/' or '\\' || relative.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var part in relative.Split('/', '\\'))
        {
            if (part is "" or ".")
            {
                continue;
            }

            if (string.Equals(part, "..", StringComparison.Ordinal))
            {
                if (parts.Count == 0)
                {
                    return null;
                }

                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return parts;
    }

    private string HostPath(string leaf)
    {
        var data = string.IsNullOrEmpty(options.HostDataDirectory) ? Path.GetFullPath(options.DataDirectory) : options.HostDataDirectory;

        // Docker Desktop on Windows takes host paths with forward slashes too, and Linux needs them.
        return $"{data.Replace('\\', '/').TrimEnd('/')}/work/{jobId}/{leaf}";
    }
}
