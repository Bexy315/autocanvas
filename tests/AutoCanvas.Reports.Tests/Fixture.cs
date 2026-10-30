using System.Text;

namespace AutoCanvas.Reports.Tests;

/// <summary>Opens report fixtures and in-memory report text.</summary>
internal static class Fixture
{
    public static ReportReadResult<TestReport> ReadTests(ReportFormat format, string relativePath)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath));
        return TestReportReader.Read(format, stream);
    }

    public static ReportReadResult<TestReport> ReadTestsText(ReportFormat format, string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return TestReportReader.Read(format, stream);
    }

    public static TestReport Succeeds(this ReportReadResult<TestReport> result)
    {
        result.Error.ShouldBeNull();
        return result.Report.ShouldNotBeNull();
    }

    public static TestCaseResult Case(this TestReport report, string name) =>
        report.Cases.Single(c => string.Equals(c.Name, name, StringComparison.Ordinal));
}
