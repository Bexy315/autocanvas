using AutoCanvas.Server.Application.Reports;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Configuration;

/// <summary>Reports need room for real test suites, and one job must not fill the database.</summary>
internal sealed class ReportOptionsValidator : IValidateOptions<ReportOptions>
{
    public const long MinBytes = 1024 * 1024;
    public const long MaxBytes = 1024L * 1024 * 1024;
    public const int MaxFiles = 1000;

    public ValidateOptionsResult Validate(string? name, ReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (options.MaxBytesPerFile is < MinBytes or > MaxBytes)
        {
            failures.Add("Reports:MaxBytesPerFile must be between 1 MiB (1048576) and 1 GiB (1073741824).");
        }

        if (options.MaxFilesPerJob is < 1 or > MaxFiles)
        {
            failures.Add($"Reports:MaxFilesPerJob must be between 1 and {MaxFiles}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
