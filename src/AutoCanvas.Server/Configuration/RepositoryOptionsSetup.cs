using AutoCanvas.Server.Application.Repositories;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// Puts the repository cache into the data directory unless configured elsewhere, and rejects git
/// timeouts that are too short to fetch anything or long enough to hang a request for minutes.
/// </summary>
internal sealed class RepositoryOptionsSetup(IOptions<ServerOptions> server, IHostEnvironment environment)
    : IPostConfigureOptions<RepositoryOptions>, IValidateOptions<RepositoryOptions>
{
    public void PostConfigure(string? name, RepositoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.CacheDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(options.CacheDirectory) ? Path.Combine(server.Value.DataDirectory, "repositories") : options.CacheDirectory,
            environment.ContentRootPath);
    }

    public ValidateOptionsResult Validate(string? name, RepositoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.GitTimeout >= TimeSpan.FromSeconds(5) && options.GitTimeout <= TimeSpan.FromMinutes(10)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Repositories:GitTimeout must be between 00:00:05 and 00:10:00, but is {options.GitTimeout}.");
    }
}
