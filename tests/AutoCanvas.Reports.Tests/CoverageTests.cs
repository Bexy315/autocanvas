using System.Text;

namespace AutoCanvas.Reports.Tests;

public sealed class CoverageTests
{
    private static ReportReadResult<CoverageReport> ReadFile(string file)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cobertura", file));
        return CoverageReportReader.Read(ReportFormat.Cobertura, stream);
    }

    private static ReportReadResult<CoverageReport> ReadText(string xml, ReportFormat format = ReportFormat.Cobertura)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return CoverageReportReader.Read(format, stream);
    }

    private static CoverageReport Succeeds(ReportReadResult<CoverageReport> result)
    {
        result.Error.ShouldBeNull();
        return result.Report.ShouldNotBeNull();
    }

    // The totals each tool writes on its own <coverage> root. ReportGenerator agrees, except for
    // istanbul, where it also counts declaration lines that appear only under <methods>.
    [Theory]
    [InlineData("coverlet.xml", 22, 28, 9, 12)]
    [InlineData("coverage-py.xml", 13, 15, 7, 8)]
    [InlineData("istanbul.xml", 5, 5, 3, 4)]
    public void Fixtures_total_like_the_tools_that_wrote_them(string file, int linesCovered, int linesValid, int branchesCovered, int branchesValid)
    {
        var report = Succeeds(ReadFile(file));

        report.Total.ShouldBe(new CoverageCounts(linesCovered, linesValid, branchesCovered, branchesValid));
        report.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void Coverlet_generated_classes_count_with_their_outer_class()
    {
        var package = Succeeds(ReadFile("coverlet.xml")).Packages.ShouldHaveSingleItem();

        package.Name.ShouldBe("Sample.Lib");
        var calculator = package.Classes.ShouldHaveSingleItem();
        calculator.Name.ShouldBe("Sample.Calculator");
        calculator.File.ShouldBe("work/lib/Calculator.cs");
        calculator.Uncovered.Select(r => r.ToString()).ShouldBe(["10-11", "19", "28-29", "45"]);
        calculator.Counts.LineRate.ShouldNotBeNull().ShouldBe(22.0 / 28, 1e-9);
    }

    [Fact]
    public void Classes_without_lines_are_left_out()
    {
        var package = Succeeds(ReadFile("coverage-py.xml")).Packages.ShouldHaveSingleItem();

        package.Name.ShouldBe(".");
        var shop = package.Classes.ShouldHaveSingleItem();
        shop.Name.ShouldBe("shop.py");
        shop.Uncovered.Select(r => r.ToString()).ShouldBe(["3", "19"]);
    }

    [Fact]
    public void Lines_under_methods_are_not_counted_twice_and_duplicates_merge()
    {
        var report = Succeeds(ReadText("""
            <coverage>
              <packages>
                <package name="B.Lib">
                  <classes>
                    <class name="B.Foo" filename="Foo.cs">
                      <methods><method name="M"><lines><line number="1" hits="1"/><line number="2" hits="0"/></lines></method></methods>
                      <lines>
                        <line number="1" hits="1"/>
                        <line number="2" hits="0" branch="True" condition-coverage="50% (1/2)"/>
                        <line number="3" hits="-1"/>
                        <line number="0" hits="5"/>
                        <line number="x" hits="5"/>
                      </lines>
                    </class>
                    <class name="B.Foo+&lt;&gt;c" filename="Foo.cs">
                      <lines><line number="2" hits="4" condition-coverage="100% (2/2)"/><line number="7" hits="0"/></lines>
                    </class>
                    <class name="B.Bar" filename="Bar.cs"><lines/></class>
                  </classes>
                </package>
                <package name="A.Lib">
                  <classes><class name="A.Only" filename="Only.cs"><lines><line number="5" hits="1" condition-coverage="garbage"/></lines></class></classes>
                </package>
                <package name="Empty"><classes/></package>
              </packages>
            </coverage>
            """));

        report.Packages.Select(p => p.Name).ShouldBe(["A.Lib", "B.Lib"]);
        var foo = report.Packages[1].Classes.ShouldHaveSingleItem();
        foo.Counts.ShouldBe(new CoverageCounts(2, 4, 2, 2));
        foo.Uncovered.Select(r => r.ToString()).ShouldBe(["3", "7"]);
        report.Packages[0].Counts.BranchRate.ShouldBeNull();
        report.Total.ShouldBe(new CoverageCounts(3, 5, 2, 2));
    }

    [Fact]
    public void Classes_outside_packages_are_kept_under_an_empty_name() =>
        Succeeds(ReadText("<coverage><class name=\"Loose\"><lines><line number=\"1\" hits=\"1\"/></lines></class></coverage>"))
            .Packages.ShouldHaveSingleItem().Name.ShouldBeEmpty();

    [Fact]
    public void Branch_counts_cannot_exceed_their_total() =>
        Succeeds(ReadText("<coverage><class name=\"C\"><lines><line number=\"1\" hits=\"1\" condition-coverage=\"300% (3/1)\"/></lines></class></coverage>"))
            .Total.ShouldBe(new CoverageCounts(1, 1, 1, 1));

    [Fact]
    public void An_empty_report_has_no_rates()
    {
        var report = Succeeds(ReadText("<coverage line-rate=\"1\"/>"));

        report.Packages.ShouldBeEmpty();
        report.Total.ShouldBe(CoverageCounts.Zero);
        report.Total.LineRate.ShouldBeNull();
    }

    [Theory]
    [InlineData("<testsuite/>", ReportFormat.Cobertura, "starts with <testsuite> instead of <coverage>")]
    [InlineData("<coverage>", ReportFormat.Cobertura, "not valid XML")]
    [InlineData("<coverage/>", ReportFormat.JUnit, "not a coverage format")]
    public void Unreadable_files_give_a_reason(string xml, ReportFormat format, string reason) =>
        ReadText(xml, format).Error.ShouldNotBeNull().ShouldContain(reason);

    [Fact]
    public void Huge_reports_keep_a_bounded_list_but_total_everything()
    {
        const int classes = ReportLimits.MaxClasses + 3;
        var xml = new StringBuilder("<coverage><packages><package name=\"P\"><classes>");
        for (var i = 0; i < classes; i++)
        {
            xml.Append(System.Globalization.CultureInfo.InvariantCulture, $"<class name=\"C{i:D6}\" filename=\"f\"><lines><line number=\"1\" hits=\"1\"/></lines></class>");
        }

        var result = ReadText(xml.Append("</classes></package></packages></coverage>").ToString());

        var report = Succeeds(result);
        report.Truncated.ShouldBeTrue();
        report.Packages[0].Classes.Count.ShouldBe(ReportLimits.MaxClasses);
        report.Total.LinesValid.ShouldBe(classes);
        result.Warnings.ShouldHaveSingleItem().ShouldContain($"{classes} classes");
    }

    [Fact]
    public void Ranges_join_consecutive_lines()
    {
        new LineRange(4, 4).ToString().ShouldBe("4");
        new LineRange(4, 9).ToString().ShouldBe("4-9");
        Reports.Xml.CoberturaReader.Ranges([]).ShouldBeEmpty();
        Reports.Xml.CoberturaReader.Ranges([1, 2, 3, 5, 7, 8]).Select(r => r.ToString()).ShouldBe(["1-3", "5", "7-8"]);
    }

    [Fact]
    public void Counts_add_up()
    {
        new CoverageCounts(1, 2, 3, 4).Add(new CoverageCounts(1, 1, 1, 1)).ShouldBe(new CoverageCounts(2, 3, 4, 5));
        Should.Throw<ArgumentNullException>(() => CoverageCounts.Zero.Add(null!));
    }

    [Fact]
    public void A_null_stream_is_a_programming_error() =>
        Should.Throw<ArgumentNullException>(() => CoverageReportReader.Read(ReportFormat.Cobertura, null!));
}
