namespace AutoCanvas.Server.Setup;

/// <summary>The setup of a fresh installation in the browser (configuration section <c>Setup</c>, ADR-0020).</summary>
internal sealed class SetupOptions
{
    public const string SectionName = "Setup";

    /// <summary>
    /// Whether a server without accounts opens the setup. Off, the first admin comes from
    /// <c>admin create</c> or the bootstrap settings only. Default: on.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where the setup writes a registration token for the runner that came with the package, which
    /// reads it from a shared volume; unset, there is no bundled runner.
    /// </summary>
    public string? RunnerTokenFile { get; set; }
}
