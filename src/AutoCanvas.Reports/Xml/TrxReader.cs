using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace AutoCanvas.Reports.Xml;

/// <summary>
/// Reads Visual Studio TRX files (<c>dotnet test --report-trx</c>, the VSTest TRX logger). Results come
/// before the test definitions that name their class and assembly, so results are kept until the
/// end of the file and then completed.
/// </summary>
internal static class TrxReader
{
    /// <summary>TRX outcomes of a test that did not run — neither passed nor failed.</summary>
    private static readonly HashSet<string> NotRun = new(StringComparer.OrdinalIgnoreCase)
    {
        "NotExecuted", "NotRunnable", "Inconclusive", "Pending", "Disconnected",
    };

    public static ReportReadResult<TestReport> Read(XmlReader reader)
    {
        var root = SafeXml.MoveToRoot(reader);
        if (!string.Equals(root, "TestRun", StringComparison.Ordinal))
        {
            return Failure.Of<TestReport>($"This is not a TRX report: it starts with <{root}> instead of <TestRun>.");
        }

        var results = new List<XElement>();
        var definitions = new Dictionary<string, (string Class, string Assembly)>(StringComparer.OrdinalIgnoreCase);
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
            }
            else if (reader.LocalName is "UnitTestResult")
            {
                results.Add(Slim(SafeXml.ReadElement(reader)));
            }
            else if (reader.LocalName is "UnitTest")
            {
                AddDefinition(SafeXml.ReadElement(reader), definitions);
            }
            else
            {
                reader.Read();
            }
        }

        var builder = new TestReportBuilder();
        foreach (var result in results)
        {
            builder.Add(ToCase(result, definitions));
        }

        return builder.Build();
    }

    /// <summary>Drops what is not used, so a run with many results keeps only what becomes a case.</summary>
    private static XElement Slim(XElement result)
    {
        foreach (var attribute in result.Attributes().Where(a => a.Name.LocalName is not ("testId" or "testName" or "outcome" or "duration")).ToList())
        {
            attribute.Remove();
        }

        return result;
    }

    private static void AddDefinition(XElement unitTest, Dictionary<string, (string Class, string Assembly)> definitions)
    {
        var id = SafeXml.Attribute(unitTest, "id");
        if (id.Length > 0 && SafeXml.Child(unitTest, "TestMethod") is { } method)
        {
            definitions[id] = (SafeXml.Attribute(method, "className"), AssemblyOf(SafeXml.Attribute(method, "codeBase")));
        }
    }

    private static TestCaseResult ToCase(XElement result, Dictionary<string, (string Class, string Assembly)> definitions)
    {
        definitions.TryGetValue(SafeXml.Attribute(result, "testId"), out var definition);
        var suite = definition.Class ?? string.Empty;
        var output = SafeXml.Child(result, "Output");
        var error = output is null ? null : SafeXml.Child(output, "ErrorInfo");
        var outcome = Outcome(SafeXml.Attribute(result, "outcome"));
        var stdout = Text.Join(output is null ? null : SafeXml.Child(output, "StdOut")?.Value, output is null ? null : SafeXml.Child(output, "StdErr")?.Value);
        var message = Text.NonEmpty(error is null ? null : SafeXml.Child(error, "Message")?.Value);

        return new TestCaseResult(
            Group: Text.NonEmpty(definition.Assembly),
            Suite: suite,
            Name: NameWithinSuite(SafeXml.Attribute(result, "testName"), suite),
            Outcome: outcome,
            Duration: TimeSpan.TryParse(SafeXml.Attribute(result, "duration"), CultureInfo.InvariantCulture, out var duration) && duration > TimeSpan.Zero ? duration : TimeSpan.Zero,
            // xUnit writes a skip reason to the output; it is the best explanation there is.
            Message: message ?? (outcome == TestOutcome.Skipped ? Text.FirstLine(stdout) : null),
            StackTrace: Text.NonEmpty(error is null ? null : SafeXml.Child(error, "StackTrace")?.Value),
            Output: stdout);
    }

    private static TestOutcome Outcome(string outcome)
    {
        if (string.Equals(outcome, "Passed", StringComparison.OrdinalIgnoreCase))
        {
            return TestOutcome.Passed;
        }

        // Failed, Error, Timeout, Aborted and anything unknown: not a pass, so it must be seen.
        return NotRun.Contains(outcome) ? TestOutcome.Skipped : TestOutcome.Failed;
    }

    /// <summary><c>Namespace.Class.Method(args)</c> becomes <c>Method(args)</c> when the class is known.</summary>
    private static string NameWithinSuite(string testName, string suite)
    {
        if (testName.Length == 0)
        {
            return "(unnamed test)";
        }

        return suite.Length > 0 && testName.StartsWith(suite + ".", StringComparison.Ordinal) ? testName[(suite.Length + 1)..] : testName;
    }

    private static string AssemblyOf(string codeBase)
    {
        var file = codeBase.Replace('\\', '/');
        file = file[(file.LastIndexOf('/') + 1)..];
        return file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }
}
