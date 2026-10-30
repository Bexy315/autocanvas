using System.Text;

namespace AutoCanvas.Reports.Tests;

public sealed class EdgeCaseTests
{
    [Theory]
    [InlineData(ReportFormat.JUnit, "", "not valid XML")]
    [InlineData(ReportFormat.JUnit, "this is not xml", "not valid XML")]
    [InlineData(ReportFormat.JUnit, "<testsuite><testcase name=\"a\">", "not valid XML")]
    [InlineData(ReportFormat.JUnit, "<coverage/>", "starts with <coverage> instead of <testsuites> or <testsuite>")]
    [InlineData(ReportFormat.Trx, "<testsuites/>", "starts with <testsuites> instead of <TestRun>")]
    [InlineData(ReportFormat.Cobertura, "<coverage/>", "not a test result format")]
    public void Unreadable_files_give_a_reason(ReportFormat format, string xml, string reason)
    {
        var result = Fixture.ReadTestsText(format, xml);

        result.Succeeded.ShouldBeFalse();
        result.Report.ShouldBeNull();
        result.Error.ShouldNotBeNull().ShouldContain(reason);
    }

    [Fact]
    public void Entities_from_a_document_type_definition_are_never_expanded()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE testsuite [ <!ENTITY secret SYSTEM "file:///etc/passwd"> ]>
            <testsuite><testcase name="&secret;"/></testsuite>
            """;

        Fixture.ReadTestsText(ReportFormat.JUnit, xml).Error.ShouldNotBeNull().ShouldContain("not valid XML");
    }

    [Fact]
    public void A_document_type_declaration_alone_is_skipped() =>
        Fixture.ReadTestsText(ReportFormat.JUnit, "<!DOCTYPE testsuite SYSTEM \"http://example.org/junit.dtd\"><testsuite><testcase name=\"a\"/></testsuite>")
            .Succeeds().Counts.Total.ShouldBe(1);

    [Fact]
    public void Nested_suites_give_each_case_its_nearest_suite()
    {
        var report = Fixture.ReadTestsText(ReportFormat.JUnit, """
            <testsuites>
              <testsuite name="outer">
                <testcase name="first"/>
                <testsuite name="inner"><testcase name="second"/></testsuite>
                <testsuite name="empty"/>
                <testcase name="third" classname="Explicit"/>
              </testsuite>
              <testcase name="orphan"/>
            </testsuites>
            """).Succeeds();

        report.Cases.Select(c => (c.Group, c.Suite, c.Name)).ShouldBe([
            ("outer", "outer", "first"),
            ("inner", "inner", "second"),
            ("outer", "Explicit", "third"),
            (null, string.Empty, "orphan"),
        ]);
        report.Case("orphan").FullName.ShouldBe("orphan");
    }

    [Theory]
    [InlineData("1.5", 1500)]
    [InlineData("2,000.5", 2_000_500)]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("-3", 0)]
    [InlineData("NaN", 0)]
    [InlineData("1e300", 0)]
    public void JUnit_times_are_seconds(string time, int milliseconds) =>
        Fixture.ReadTestsText(ReportFormat.JUnit, $"<testsuite><testcase name=\"a\" time=\"{time}\"/></testsuite>")
            .Succeeds().Cases[0].Duration.ShouldBe(TimeSpan.FromMilliseconds(milliseconds));

    [Fact]
    public void Missing_names_and_messages_are_tolerated()
    {
        var report = Fixture.ReadTestsText(ReportFormat.JUnit, "<testsuite><testcase><failure/></testcase><testcase><skipped>  </skipped></testcase></testsuite>").Succeeds();

        report.Cases[0].ShouldBe(new TestCaseResult(null, string.Empty, "(unnamed test)", TestOutcome.Failed, TimeSpan.Zero, null, null, null));
        report.Cases[1].Outcome.ShouldBe(TestOutcome.Skipped);
        report.Cases[1].Message.ShouldBeNull();
    }

    [Fact]
    public void A_single_rerun_is_described_in_the_singular() =>
        Fixture.ReadTestsText(ReportFormat.JUnit, "<testsuite><testcase name=\"a\"><flakyError/></testcase></testsuite>")
            .Succeeds().Cases[0].Message.ShouldBe("Passed after 1 failed attempt.");

    [Fact]
    public void JUnit_output_joins_stdout_and_stderr() =>
        Fixture.ReadTestsText(ReportFormat.JUnit, "<testsuite><testcase name=\"a\"><system-out>out</system-out><system-err>err</system-err></testcase></testsuite>")
            .Succeeds().Cases[0].Output.ShouldBe("out\nerr");

    [Theory]
    [InlineData("Passed", TestOutcome.Passed)]
    [InlineData("passed", TestOutcome.Passed)]
    [InlineData("Failed", TestOutcome.Failed)]
    [InlineData("Timeout", TestOutcome.Failed)]
    [InlineData("Aborted", TestOutcome.Failed)]
    [InlineData("SomethingNew", TestOutcome.Failed)]
    [InlineData("NotExecuted", TestOutcome.Skipped)]
    [InlineData("Inconclusive", TestOutcome.Skipped)]
    public void TRX_outcomes_map_to_three_results(string outcome, TestOutcome expected) =>
        Fixture.ReadTestsText(ReportFormat.Trx, $"<TestRun><Results><UnitTestResult testName=\"T.a\" outcome=\"{outcome}\"/></Results></TestRun>")
            .Succeeds().Cases[0].Outcome.ShouldBe(expected);

    [Fact]
    public void TRX_without_definitions_keeps_the_full_name()
    {
        var report = Fixture.ReadTestsText(ReportFormat.Trx, """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="x" testName="Ns.Class.Method" outcome="Passed" duration="00:00:01.5000000"/>
                <UnitTestResult outcome="Failed" duration="garbage">
                  <Output><StdErr>boom</StdErr><ErrorInfo><Message>  </Message></ErrorInfo></Output>
                </UnitTestResult>
              </Results>
              <TestDefinitions><UnitTest id="other"/><UnitTest><TestMethod className="C"/></UnitTest></TestDefinitions>
            </TestRun>
            """).Succeeds();

        report.Cases[0].ShouldBe(new TestCaseResult(null, string.Empty, "Ns.Class.Method", TestOutcome.Passed, TimeSpan.FromSeconds(1.5), null, null, null));
        report.Cases[1].ShouldBe(new TestCaseResult(null, string.Empty, "(unnamed test)", TestOutcome.Failed, TimeSpan.Zero, null, null, "boom"));
    }

    [Fact]
    public void TRX_assemblies_come_from_any_path_style()
    {
        var report = Fixture.ReadTestsText(ReportFormat.Trx, """
            <TestRun>
              <Results><UnitTestResult testId="1" testName="C.M" outcome="Passed"/><UnitTestResult testId="2" testName="D.M" outcome="Passed"/></Results>
              <TestDefinitions>
                <UnitTest id="1"><TestMethod className="C" codeBase="C:\build\My.Tests.dll"/></UnitTest>
                <UnitTest id="2"><TestMethod className="D" codeBase="tests.jar"/></UnitTest>
              </TestDefinitions>
            </TestRun>
            """).Succeeds();

        report.Cases.Select(c => (c.Group, c.Suite, c.Name)).ShouldBe([("My.Tests", "C", "M"), ("tests.jar", "D", "M")]);
    }

    [Fact]
    public void A_byte_order_mark_is_accepted()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("<testsuite><testcase name=\"a\"/></testsuite>")).ToArray();
        using var stream = new MemoryStream(bytes);

        TestReportReader.Read(ReportFormat.JUnit, stream).Succeeds().Counts.Total.ShouldBe(1);
    }

    [Fact]
    public void Long_texts_are_cut()
    {
        var long1 = new string('m', ReportLimits.MaxMessage + 10);
        var report = Fixture.ReadTestsText(ReportFormat.JUnit, $"<testsuite><testcase name=\"{new string('n', 2000)}\"><failure message=\"{long1}\">{new string('s', 9000)}</failure><system-out>{new string('o', 5000)}</system-out></testcase></testsuite>").Succeeds();

        var cut = report.Cases[0];
        cut.Name.Length.ShouldBe(ReportLimits.MaxName);
        cut.Name.ShouldEndWith("…");
        cut.Message!.Length.ShouldBe(ReportLimits.MaxMessage);
        cut.StackTrace!.Length.ShouldBe(ReportLimits.MaxStackTrace);
        cut.Output!.Length.ShouldBe(ReportLimits.MaxOutput);
    }

    [Fact]
    public void Huge_reports_keep_a_bounded_list_but_count_everything()
    {
        const int total = ReportLimits.MaxCases + 5;
        var xml = new StringBuilder("<testsuite name=\"big\">");
        for (var i = 0; i < total; i++)
        {
            xml.Append(i % 2 == 0 ? "<testcase name=\"t\" time=\"0.001\"/>" : "<testcase name=\"t\"><failure/></testcase>");
        }

        var result = Fixture.ReadTestsText(ReportFormat.JUnit, xml.Append("</testsuite>").ToString());

        var report = result.Succeeds();
        report.Truncated.ShouldBeTrue();
        report.Cases.Count.ShouldBe(ReportLimits.MaxCases);
        report.Counts.Total.ShouldBe(total);
        report.Counts.Passed.ShouldBe((total + 1) / 2);
        report.Counts.Failed.ShouldBe(total / 2);
        result.Warnings.ShouldHaveSingleItem().ShouldContain($"{total} tests");
    }

    [Fact]
    public void A_null_stream_is_a_programming_error() =>
        Should.Throw<ArgumentNullException>(() => TestReportReader.Read(ReportFormat.JUnit, null!));
}
