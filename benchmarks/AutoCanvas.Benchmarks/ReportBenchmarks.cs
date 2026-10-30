using System.Globalization;
using System.Text;
using AutoCanvas.Reports;
using BenchmarkDotNet.Attributes;

namespace AutoCanvas.Benchmarks;

/// <summary>
/// Reading test reports of a large suite: 10,000 tests, one in ten failing with a stack trace and
/// output, as JUnit and as TRX.
/// </summary>
[MemoryDiagnoser]
public class ReportBenchmarks
{
    private const int Tests = 10_000;

    private byte[] _junit = [];
    private byte[] _trx = [];

    [GlobalSetup]
    public void Setup()
    {
        _junit = Encoding.UTF8.GetBytes(JUnit());
        _trx = Encoding.UTF8.GetBytes(Trx());
    }

    [Benchmark]
    public int ReadJUnit()
    {
        using var stream = new MemoryStream(_junit);
        return TestReportReader.Read(ReportFormat.JUnit, stream).Report!.Counts.Total;
    }

    [Benchmark]
    public int ReadTrx()
    {
        using var stream = new MemoryStream(_trx);
        return TestReportReader.Read(ReportFormat.Trx, stream).Report!.Counts.Total;
    }

    private static string JUnit()
    {
        var xml = new StringBuilder("<testsuites><testsuite name=\"Big\">");
        for (var i = 0; i < Tests; i++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<testcase classname=\"com.example.Class{i / 100}\" name=\"test{i}\" time=\"0.012\">");
            if (i % 10 == 0)
            {
                xml.Append(CultureInfo.InvariantCulture, $"<failure message=\"expected {i}\">{StackTrace}</failure><system-out>output of test {i}</system-out>");
            }

            xml.Append("</testcase>");
        }

        return xml.Append("</testsuite></testsuites>").ToString();
    }

    private static string Trx()
    {
        var xml = new StringBuilder("<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><Results>");
        for (var i = 0; i < Tests; i++)
        {
            var failed = i % 10 == 0;
            xml.Append(CultureInfo.InvariantCulture, $"<UnitTestResult testId=\"id{i}\" testName=\"Ns.Class{i / 100}.Test{i}\" outcome=\"{(failed ? "Failed" : "Passed")}\" duration=\"00:00:00.0120000\">");
            if (failed)
            {
                xml.Append(CultureInfo.InvariantCulture, $"<Output><StdOut>output of test {i}</StdOut><ErrorInfo><Message>expected {i}</Message><StackTrace>{StackTrace}</StackTrace></ErrorInfo></Output>");
            }

            xml.Append("</UnitTestResult>");
        }

        xml.Append("</Results><TestDefinitions>");
        for (var i = 0; i < Tests; i++)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<UnitTest id=\"id{i}\"><TestMethod className=\"Ns.Class{i / 100}\" codeBase=\"/work/bin/Big.Tests.dll\" name=\"Test{i}\"/></UnitTest>");
        }

        return xml.Append("</TestDefinitions></TestRun>").ToString();
    }

    private static string StackTrace => string.Concat(Enumerable.Repeat("   at Ns.Some.Method(String argument) in /work/src/Some.cs:line 42\n", 20));
}
