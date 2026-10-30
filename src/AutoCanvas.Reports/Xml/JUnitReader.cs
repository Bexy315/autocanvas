using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace AutoCanvas.Reports.Xml;

/// <summary>
/// Reads JUnit XML, the de-facto standard written by Maven Surefire, Gradle, pytest, jest-junit,
/// go-junit-report and many more. The root is <c>&lt;testsuites&gt;</c> or a single
/// <c>&lt;testsuite&gt;</c>; suites may nest. Each <c>&lt;testcase&gt;</c> is read on its own, so
/// memory does not grow with the file.
/// </summary>
internal static class JUnitReader
{
    private static readonly HashSet<string> RerunElements = new(StringComparer.Ordinal)
    {
        "flakyFailure", "flakyError", "rerunFailure", "rerunError",
    };

    public static ReportReadResult<TestReport> Read(XmlReader reader)
    {
        var root = SafeXml.MoveToRoot(reader);
        if (root is not ("testsuites" or "testsuite"))
        {
            return Failure.Of<TestReport>($"This is not a JUnit report: it starts with <{root}> instead of <testsuites> or <testsuite>.");
        }

        var builder = new TestReportBuilder();
        var suites = new Stack<(int Depth, string Name)>();
        while (!reader.EOF)
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "testcase" })
            {
                builder.Add(ToCase(SafeXml.ReadElement(reader), suites.Count > 0 ? suites.Peek().Name : null));
                continue;
            }

            Track(reader, suites);
            reader.Read();
        }

        return builder.Build();
    }

    /// <summary>Keeps the stack of enclosing suites in step with the reader.</summary>
    private static void Track(XmlReader reader, Stack<(int Depth, string Name)> suites)
    {
        if (reader.LocalName is not "testsuite")
        {
            return;
        }

        if (reader.NodeType == XmlNodeType.Element && !reader.IsEmptyElement)
        {
            suites.Push((reader.Depth, reader.GetAttribute("name") ?? reader.GetAttribute("file") ?? string.Empty));
        }
        else if (reader.NodeType == XmlNodeType.EndElement && suites.Count > 0 && suites.Peek().Depth == reader.Depth)
        {
            suites.Pop();
        }
    }

    private static TestCaseResult ToCase(XElement testCase, string? suiteName)
    {
        var name = Text.NonEmpty(SafeXml.Attribute(testCase, "name")) ?? "(unnamed test)";
        var suite = Text.NonEmpty(SafeXml.Attribute(testCase, "classname")) ?? suiteName ?? string.Empty;
        var output = Text.Join(SafeXml.Child(testCase, "system-out")?.Value, SafeXml.Child(testCase, "system-err")?.Value);
        var duration = Seconds(SafeXml.Attribute(testCase, "time"));
        var group = Text.NonEmpty(suiteName);

        var problem = SafeXml.Child(testCase, "failure") ?? SafeXml.Child(testCase, "error");
        if (problem is not null)
        {
            var text = Text.NonEmpty(problem.Value);
            var message = Text.NonEmpty(SafeXml.Attribute(problem, "message")) ?? Text.FirstLine(text);
            return new TestCaseResult(group, suite, name, TestOutcome.Failed, duration, message, text, output);
        }

        if (SafeXml.Child(testCase, "skipped") is { } skipped)
        {
            var message = Text.NonEmpty(SafeXml.Attribute(skipped, "message")) ?? Text.FirstLine(skipped.Value);
            return new TestCaseResult(group, suite, name, TestOutcome.Skipped, duration, message, null, output);
        }

        var reruns = testCase.Elements().Count(e => RerunElements.Contains(e.Name.LocalName));
        var note = reruns > 0 ? $"Passed after {reruns} failed {(reruns == 1 ? "attempt" : "attempts")}." : null;
        return new TestCaseResult(group, suite, name, TestOutcome.Passed, duration, note, null, output);
    }

    /// <summary>
    /// A <c>time</c> attribute in seconds. Some Surefire versions write a thousands separator
    /// (<c>1,234.5</c>); a comma is therefore dropped, never read as a decimal point.
    /// </summary>
    private static TimeSpan Seconds(string value)
    {
        var cleaned = value.Replace(",", string.Empty, StringComparison.Ordinal);
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }
}
