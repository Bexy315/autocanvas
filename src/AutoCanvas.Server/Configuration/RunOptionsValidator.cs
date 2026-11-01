using AutoCanvas.Server.Application.Runs;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>Retention is a number of days (or 0 to keep runs forever), and a number of runs always kept.</summary>
internal sealed class RunOptionsValidator : IValidateOptions<RunOptions>
{
    public const int MaxDays = 3650;
    public const int MaxKept = 1000;

    public ValidateOptionsResult Validate(string? name, RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.RetentionDays is < 0 or > MaxDays)
        {
            failures.Add($"Runs:RetentionDays must be 0 (keep forever) or a number of days up to {MaxDays}, e.g. 90.");
        }

        if (options.KeepPerAutomation is < 0 or > MaxKept)
        {
            failures.Add($"Runs:KeepPerAutomation must be between 0 and {MaxKept}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
