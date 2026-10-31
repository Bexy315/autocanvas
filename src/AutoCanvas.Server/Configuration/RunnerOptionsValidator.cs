using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Application.Runs;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// Heartbeats must come often enough that one late or lost heartbeat does not end a lease, but not
/// so often that runners flood the server.
/// </summary>
internal sealed class RunnerOptionsValidator(IOptions<QueueOptions> queue) : IValidateOptions<RunnerOptions>
{
    public ValidateOptionsResult Validate(string? name, RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var lease = queue.Value.LeaseDuration;
        return options.HeartbeatInterval >= TimeSpan.FromSeconds(1) && options.HeartbeatInterval * 2 <= lease
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Runners:HeartbeatInterval must be at least 00:00:01 and at most half of Queue:LeaseDuration ({lease}).");
    }
}
