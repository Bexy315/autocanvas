using AutoCanvas.Runner.Connection;
using AutoCanvas.Runner.Docker;

namespace AutoCanvas.Runner.Execution;

/// <summary>What every job of the runner works with.</summary>
/// <param name="Runtime">The container engine.</param>
/// <param name="Outbox">Messages for the server.</param>
/// <param name="Server">The channel to the server, for report uploads.</param>
/// <param name="Options">The runner's settings.</param>
/// <param name="Time">The clock for log lines and results.</param>
internal sealed record JobServices(IContainerRuntime Runtime, Outbox Outbox, ServerChannel Server, RunnerOptions Options, TimeProvider Time)
{
    /// <summary>How long a stopped step's shell has after SIGTERM before its container is restarted.</summary>
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromSeconds(10);
}
