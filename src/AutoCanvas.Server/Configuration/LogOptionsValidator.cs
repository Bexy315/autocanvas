using AutoCanvas.Server.Application.Runs;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>A job needs room for a useful log, and one job must not fill the database.</summary>
internal sealed class LogOptionsValidator : IValidateOptions<LogOptions>
{
    public const long Min = 1024 * 1024;
    public const long Max = 1024L * 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, LogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.MaxBytesPerJob is >= Min and <= Max
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Logs:MaxBytesPerJob must be between 1 MiB (1048576) and 1 GiB (1073741824).");
    }
}
