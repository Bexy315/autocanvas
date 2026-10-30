using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Parsing;

namespace AutoCanvas.Core.Tests.Parsing;

public sealed class SupportTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("needs", "need", 1)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("abc", "", 3)]
    public void Distance_counts_single_character_edits(string a, string b, int expected)
    {
        Suggestions.Distance(a, b).ShouldBe(expected);
        Suggestions.Distance(b, a).ShouldBe(expected);
    }

    [Fact]
    public void No_suggestion_when_nothing_is_close() =>
        Suggestions.ForKey("completely_different", ["needs", "steps"]).ShouldBeEmpty();

    [Fact]
    public void Diagnostics_without_location_or_path_print_compactly() =>
        new Diagnostic(DiagnosticSeverity.Warning, "AC0001", "Message.", string.Empty, null).ToString().ShouldBe("AC0001 Message.");

    [Fact]
    public void Parse_rejects_null() =>
        Should.Throw<ArgumentNullException>(() => AutomationParser.Parse(null!));
}
