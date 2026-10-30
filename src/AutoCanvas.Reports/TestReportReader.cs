using System.Xml;
using AutoCanvas.Reports.Xml;

namespace AutoCanvas.Reports;

/// <summary>
/// Reads test result files (TRX and JUnit XML) into a <see cref="TestReport"/>. Reports are untrusted
/// input: they are read as a stream with DTDs disabled, and every problem is returned as an error
/// message instead of an exception.
/// </summary>
public static class TestReportReader
{
    /// <summary>Reads one test result file.</summary>
    /// <param name="format">The file's format, as declared in the automation's <c>reports</c>.</param>
    /// <param name="content">The file's content; it is read once and not closed.</param>
    /// <returns>The report, or why it could not be read.</returns>
    public static ReportReadResult<TestReport> Read(ReportFormat format, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (format is not (ReportFormat.Trx or ReportFormat.JUnit))
        {
            return Failure.Of<TestReport>($"{format} is not a test result format; read it as coverage.");
        }

        var name = format == ReportFormat.Trx ? "TRX" : "JUnit";
        try
        {
            using var reader = SafeXml.Open(content);
            return format == ReportFormat.Trx ? TrxReader.Read(reader) : JUnitReader.Read(reader);
        }
        catch (XmlException exception)
        {
            return Failure.Of<TestReport>(SafeXml.Describe(exception, name));
        }
    }
}
