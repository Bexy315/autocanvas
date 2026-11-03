namespace AutoCanvas.Runner;

/// <summary>The runner's settings (configuration section <c>Runner</c>, environment <c>AUTOCANVAS_RUNNER__…</c>).</summary>
internal sealed class RunnerOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Runner";

    /// <summary>The server's runner endpoint, such as <c>http://autocanvas:5081</c>.</summary>
    public Uri? Server { get; set; }

    /// <summary>Where the runner keeps its credential and the jobs' workspaces.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>
    /// The path of <see cref="DataDirectory"/> as the Docker host sees it, when the runner itself
    /// runs in a container: Docker resolves bind mounts on the host, not in the runner's container.
    /// Empty when the runner runs directly on the host.
    /// </summary>
    public string? HostDataDirectory { get; set; }

    /// <summary>
    /// A file a registration token appears in, for a runner that registers itself (the bundled
    /// runner of the compose package, ADR-0020): without a credential, the runner waits for the file,
    /// registers with the token and runs. Unset, an unregistered runner stops and says so.
    /// </summary>
    public string? RegistrationTokenFile { get; set; }

    /// <summary>The name a runner that registers itself takes; the machine's name when unset.</summary>
    public string? Name { get; set; }

    /// <summary>The labels the runner offers; jobs whose <c>runs_on</c> it has all of come to it.</summary>
    public IList<string> Labels { get; } = [];

    /// <summary>How many jobs run at the same time.</summary>
    public int MaxConcurrentJobs { get; set; } = 1;

    /// <summary>The image of jobs that name none.</summary>
    public string DefaultImage { get; set; } = "ubuntu:24.04";

    /// <summary>The image checkouts run git in. Pinned: a checkout is only as repeatable as its git.</summary>
    public string GitImage { get; set; } = "alpine/git:v2.52.0";

    /// <summary>Rootless BuildKit, which <c>docker_build</c> builds with. Pinned for the same reason.</summary>
    public string BuildKitImage { get; set; } = "moby/buildkit:v0.34.0-rootless";

    /// <summary>The Docker volume BuildKit keeps its cache in, shared by the builds of this runner.</summary>
    public string BuildCacheVolume { get; set; } = "autocanvas-buildkit-cache";

    /// <summary>How long a stopping runner lets running jobs finish before it cancels them.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The credential file inside <see cref="DataDirectory"/>.</summary>
    public string CredentialFile => Path.Combine(DataDirectory, "credential.json");

    /// <summary>Where jobs get their directories, inside <see cref="DataDirectory"/>.</summary>
    public string WorkRoot => Path.Combine(DataDirectory, "work");

    /// <summary>The labels to announce: the configured ones, or <c>linux</c> when none are set.</summary>
    public IReadOnlyList<string> EffectiveLabels => Labels.Count == 0 ? ["linux"] : [.. Labels.Distinct(StringComparer.Ordinal)];
}
