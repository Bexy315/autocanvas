using AutoCanvas.Server.Application.Runners;
using AutoCanvas.Server.Application.Runs;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// Heartbeats must come often enough that one late or lost heartbeat does not end a lease, but not
/// so often that runners flood the server; the runner address, if set, is an absolute http(s) URL.
/// </summary>
internal sealed class RunnerOptionsValidator(IOptions<QueueOptions> queue) : IValidateOptions<RunnerOptions>
{
    public ValidateOptionsResult Validate(string? name, RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var lease = queue.Value.LeaseDuration;
        if (options.HeartbeatInterval < TimeSpan.FromSeconds(1) || options.HeartbeatInterval * 2 > lease)
        {
            return ValidateOptionsResult.Fail($"Runners:HeartbeatInterval must be at least 00:00:01 and at most half of Queue:LeaseDuration ({lease}).");
        }

        return options.PublicUrl is { } url && (!url.IsAbsoluteUri
            || !(string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) || string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
            ? ValidateOptionsResult.Fail($"Runners:PublicUrl must be an absolute http or https URL such as http://autocanvas.example.org:5081, but is '{url}'.")
            : ValidateOptionsResult.Success;
    }
}
