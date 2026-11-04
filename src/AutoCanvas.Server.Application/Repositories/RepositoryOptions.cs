namespace AutoCanvas.Server.Application.Repositories;

/// <summary>How the server reads repositories (configuration section <c>Repositories</c>, ADR-0021).</summary>
public sealed class RepositoryOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Repositories";

    /// <summary>
    /// Where the server keeps a shallow copy of each repository; the host sets it to
    /// <c>repositories</c> in its data directory when unset.
    /// </summary>
    public string CacheDirectory { get; set; } = string.Empty;

    /// <summary>How long one git call may take. Default: one minute.</summary>
    public TimeSpan GitTimeout { get; set; } = TimeSpan.FromMinutes(1);
}
