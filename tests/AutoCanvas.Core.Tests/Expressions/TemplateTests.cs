using AutoCanvas.Core.Expressions;

namespace AutoCanvas.Core.Tests.Expressions;

public sealed class TemplateTests
{
    private static readonly DictionaryResolver Values = new(new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["run.commit_short_sha"] = "abc1234",
        ["trigger.branch"] = "main",
        ["vars.NOTHING"] = null,
    });

    private static string Render(string text)
    {
        var (template, errors) = Template.Parse(text);
        errors.ShouldBeEmpty();
        return template.Render(Values);
    }

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("", "")]
    [InlineData("${{ run.commit_short_sha }}", "abc1234")]
    [InlineData("v-${{run.commit_short_sha}}-${{ trigger.branch }}!", "v-abc1234-main!")]
    [InlineData("deploy=${{ trigger.branch == 'main' }}", "deploy=true")]
    [InlineData("[${{ vars.NOTHING }}][${{ vars.UNKNOWN }}]", "[][]")]
    [InlineData("costs $5 {{ not an expression }}", "costs $5 {{ not an expression }}")]
    public void Templates_replace_expressions_with_their_values(string text, string expected) =>
        Render(text).ShouldBe(expected);

    [Fact]
    public void Templates_know_whether_they_contain_expressions()
    {
        Template.Parse("plain").Template.HasExpressions.ShouldBeFalse();
        Template.Parse("a ${{ vars.X }}").Template.HasExpressions.ShouldBeTrue();
    }

    [Fact]
    public void Expression_parts_remember_their_position()
    {
        var (template, _) = Template.Parse("ab${{ vars.X }}");

        template.Parts.OfType<ExpressionPart>().ShouldHaveSingleItem().Offset.ShouldBe(2);
    }

    [Fact]
    public void An_unclosed_expression_is_an_error_and_stays_text()
    {
        var (template, errors) = Template.Parse("a ${{ vars.X");

        errors.ShouldHaveSingleItem().Offset.ShouldBe(2);
        template.Render(Values).ShouldBe("a ${{ vars.X");
    }

    [Fact]
    public void Every_invalid_expression_is_reported_with_its_position_in_the_text()
    {
        var (template, errors) = Template.Parse("${{ == }} and ${{ vars. }} and ${{ run.commit_short_sha }}");

        errors.Select(e => e.Offset).ShouldBe([4, 18]);
        template.Render(Values).ShouldBe("${{ == }} and ${{ vars. }} and abc1234");
    }

    [Fact]
    public void Arguments_must_not_be_null()
    {
        Should.Throw<ArgumentNullException>(() => Template.Parse(null!));
        Should.Throw<ArgumentNullException>(() => Template.Parse("x").Template.Render(null!));
    }
}
