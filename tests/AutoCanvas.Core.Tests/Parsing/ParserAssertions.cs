using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using AutoCanvas.Core.Parsing;

namespace AutoCanvas.Core.Tests.Parsing;

/// <summary>Helpers to parse snippets and check their outcome.</summary>
internal static class ParserAssertions
{
    /// <summary>A minimal valid automation with one job, for snippets that only change one part.</summary>
    public const string MinimalJob = """
        version: 1
        jobs:
          build:
            steps:
              - run: make
        """;

    public static AutomationDefinition ParseValid(string yaml)
    {
        var result = AutomationParser.Parse(yaml);
        result.Diagnostics.ShouldBeEmpty(string.Join(Environment.NewLine, result.Diagnostics));
        return result.Definition.ShouldNotBeNull();
    }

    public static StepJob ParseJob(string jobBody) => (StepJob)ParseValid(WithJob(jobBody)).Jobs.Single();

    /// <summary>Asserts the snippet fails with exactly one diagnostic with this code and path.</summary>
    public static Diagnostic SingleError(string yaml, string code, string path)
    {
        var result = AutomationParser.Parse(yaml);
        result.Succeeded.ShouldBeFalse();
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem(string.Join(Environment.NewLine, result.Diagnostics));
        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        diagnostic.Code.ShouldBe(code);
        diagnostic.Path.ShouldBe(path);
        return diagnostic;
    }

    /// <summary>An automation whose only job, <c>build</c>, has the given body (indented by the caller's YAML).</summary>
    public static string WithJob(string jobBody) =>
        "version: 1\njobs:\n  build:\n" + Indent(jobBody, "    ");

    public static string Indent(string text, string prefix) =>
        string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Select(line => line.Length == 0 ? line : prefix + line));
}
