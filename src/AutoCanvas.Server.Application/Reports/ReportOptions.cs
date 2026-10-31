namespace AutoCanvas.Server.Application.Reports;

/// <summary>Limits of uploaded report files (configuration section <c>Reports</c>).</summary>
public sealed class ReportOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "Reports";

    /// <summary>The largest report file a runner may upload. Default: 50 MiB.</summary>
    public long MaxBytesPerFile { get; set; } = 50L * 1024 * 1024;

    /// <summary>How many report files one job attempt may upload. Default: 100.</summary>
    public int MaxFilesPerJob { get; set; } = 100;
}
