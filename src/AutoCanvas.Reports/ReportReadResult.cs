namespace AutoCanvas.Reports;

/// <summary>The outcome of reading a report file: the report, or why it could not be read.</summary>
/// <typeparam name="T">The kind of report.</typeparam>
/// <param name="Report">The report, or <see langword="null"/> when the file could not be read.</param>
/// <param name="Error">Why the file could not be read, for the user; <see langword="null"/> on success.</param>
/// <param name="Warnings">Things that were read, but not completely, such as a cut list of cases.</param>
public sealed record ReportReadResult<T>(T? Report, string? Error, IReadOnlyList<string> Warnings)
    where T : class
{
    /// <summary>Whether the file was read.</summary>
    public bool Succeeded => Report is not null;
}
