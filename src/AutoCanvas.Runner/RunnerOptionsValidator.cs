using Microsoft.Extensions.Options;

namespace AutoCanvas.Runner;

/// <summary>Settings that cannot work are refused when the runner starts, not when the first job comes.</summary>
internal sealed class RunnerOptionsValidator : IValidateOptions<RunnerOptions>
{
    public const int MaxJobs = 64;

    public ValidateOptionsResult Validate(string? name, RunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.Server is not { IsAbsoluteUri: true } server || (!string.Equals(server.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !string.Equals(server.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            failures.Add("Runner:Server must be the server's runner endpoint, an http or https address such as http://autocanvas:5081.");
        }

        if (string.IsNullOrWhiteSpace(options.DataDirectory))
        {
            failures.Add("Runner:DataDirectory must be set.");
        }

        if (options.MaxConcurrentJobs is < 1 or > MaxJobs)
        {
            failures.Add($"Runner:MaxConcurrentJobs must be between 1 and {MaxJobs}.");
        }

        foreach (var (key, value) in new[] { ("DefaultImage", options.DefaultImage), ("GitImage", options.GitImage), ("BuildKitImage", options.BuildKitImage), ("BuildCacheVolume", options.BuildCacheVolume) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                failures.Add($"Runner:{key} must be set.");
            }
        }

        if (options.ShutdownTimeout < TimeSpan.Zero || options.ShutdownTimeout > TimeSpan.FromDays(1))
        {
            failures.Add("Runner:ShutdownTimeout must be between 0 and one day.");
        }

        if (options.Labels.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Runner:Labels must not contain empty labels.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
