using AutoCanvas.Reports;

namespace AutoCanvas.Reports.Tests;

public sealed class ReportFormatsTests
{
    [Theory]
    [InlineData("junit", ReportFormat.JUnit)]
    [InlineData("TRX", ReportFormat.Trx)]
    [InlineData(" cobertura ", ReportFormat.Cobertura)]
    public void Known_names_resolve_ignoring_case_and_whitespace(string name, ReportFormat expected)
    {
        ReportFormats.TryParse(name, out var format).ShouldBeTrue();
        format.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sarif")]
    public void Unknown_names_do_not_resolve(string? name) =>
        ReportFormats.TryParse(name, out _).ShouldBeFalse();

    [Fact]
    public void Every_format_has_exactly_one_name() =>
        ReportFormats.Names.Count.ShouldBe(Enum.GetValues<ReportFormat>().Length);
}
