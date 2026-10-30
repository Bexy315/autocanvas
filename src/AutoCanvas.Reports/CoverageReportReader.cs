using System.Xml;
using AutoCanvas.Reports.Xml;

namespace AutoCanvas.Reports;

/// <summary>
/// Reads coverage files (Cobertura XML) into a <see cref="CoverageReport"/>. Like
/// <see cref="TestReportReader"/>, it streams untrusted input with DTDs disabled and returns problems
/// as error messages.
/// </summary>
public static class CoverageReportReader
{
    /// <summary>Reads one coverage file.</summary>
    /// <param name="format">The file's format, as declared in the automation's <c>reports</c>.</param>
    /// <param name="content">The file's content; it is read once and not closed.</param>
    /// <returns>The report, or why it could not be read.</returns>
    public static ReportReadResult<CoverageReport> Read(ReportFormat format, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (format != ReportFormat.Cobertura)
        {
            return Failure.Of<CoverageReport>($"{format} is not a coverage format; read it as test results.");
        }

        try
        {
            using var reader = SafeXml.Open(content);
            return CoberturaReader.Read(reader);
        }
        catch (XmlException exception)
        {
            return Failure.Of<CoverageReport>(SafeXml.Describe(exception, "Cobertura"));
        }
    }
}
