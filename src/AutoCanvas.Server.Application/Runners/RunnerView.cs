using AutoCanvas.Persistence.Runners;

namespace AutoCanvas.Server.Application.Runners;

/// <summary>A registered runner as administrators see it.</summary>
/// <param name="Id">The runner's id.</param>
/// <param name="Name">Its display name.</param>
/// <param name="CredentialHint">The start and last characters of its credential.</param>
/// <param name="Capabilities">What it announced on its last connect.</param>
/// <param name="IsPaused">Whether it is kept from taking new jobs.</param>
/// <param name="IsOnline">Whether it was heard from within the last two heartbeat intervals.</param>
/// <param name="LastSeenAt">When it was last heard from.</param>
/// <param name="CreatedAt">When it registered.</param>
/// <param name="CreatedBy">Who created the token it registered with.</param>
/// <param name="Version">Raised with every change; the API's ETag.</param>
public sealed record RunnerView(
    Guid Id,
    string Name,
    string CredentialHint,
    RunnerCapabilities Capabilities,
    bool IsPaused,
    bool IsOnline,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    int Version)
{
    internal static RunnerView From(Runner runner, DateTimeOffset onlineSince) => new(
        runner.Id,
        runner.Name,
        runner.CredentialHint,
        RunnerCapabilities.Read(runner.Capabilities),
        runner.IsPaused,
        runner.LastSeenAt >= onlineSince,
        runner.LastSeenAt,
        runner.CreatedAt,
        runner.CreatedByName,
        runner.Version);
}
