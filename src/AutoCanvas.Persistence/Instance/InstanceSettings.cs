namespace AutoCanvas.Persistence.Instance;

/// <summary>
/// Settings of the whole installation that admins change in the web UI, such as the addresses it is
/// reached at (ADR-0020). A value set in the server's configuration wins over the one stored here.
/// There is at most one row.
/// </summary>
public sealed class InstanceSettings
{
    /// <summary>The only row's id.</summary>
    public const int SingletonId = 1;

    /// <summary>Longest address stored.</summary>
    public const int MaxUrlLength = 2048;

    /// <summary>Always <see cref="SingletonId"/>.</summary>
    public required int Id { get; init; }

    /// <summary>The address people and other systems reach the server at, e.g. behind a proxy.</summary>
    public Uri? PublicUrl { get; set; }

    /// <summary>The address runners reach the server's runner endpoint at.</summary>
    public Uri? RunnerUrl { get; set; }

    /// <summary>When the settings last changed (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who changed them last; <c>null</c> once that user is deleted.</summary>
    public Guid? UpdatedBy { get; set; }
}
