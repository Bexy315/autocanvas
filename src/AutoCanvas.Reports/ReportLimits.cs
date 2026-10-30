namespace AutoCanvas.Reports;

/// <summary>
/// Bounds that keep a single report from using unbounded memory: lists are cut after
/// <see cref="MaxCases"/> test cases or <see cref="MaxClasses"/> coverage classes, long texts after
/// the lengths below (with an ellipsis).
/// </summary>
public static class ReportLimits
{
    /// <summary>Most test cases kept per file. Counts still include every test.</summary>
    public const int MaxCases = 100_000;

    /// <summary>Longest group, suite or test name kept.</summary>
    public const int MaxName = 1000;

    /// <summary>Longest failure or skip message kept.</summary>
    public const int MaxMessage = 4000;

    /// <summary>Longest stack trace kept.</summary>
    public const int MaxStackTrace = 8000;

    /// <summary>Longest test output kept.</summary>
    public const int MaxOutput = 4000;

    /// <summary>Most coverage classes kept in detail per file. Totals still include every class.</summary>
    public const int MaxClasses = 50_000;
}
