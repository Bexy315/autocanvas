using AutoCanvas.Core.Expressions;

namespace AutoCanvas.Core.Tests.Expressions;

public sealed class ExpressionTests
{
    private static readonly DictionaryResolver Values = new(new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["trigger.branch"] = "main",
        ["vars.DEPLOY"] = "true",
        ["vars.EMPTY"] = string.Empty,
        ["vars.ZERO"] = "0",
        ["vars.NOTHING"] = null,
        ["jobs.my-job.outputs.count"] = "3",
        ["vars.QUOTE"] = "it's",
    });

    private static string? Evaluate(string text)
    {
        var (expression, error) = ExpressionSyntax.Parse(text);
        error.ShouldBeNull();
        return ExpressionEvaluator.Evaluate(expression!, Values);
    }

    [Theory]
    [InlineData("trigger.branch", "main")]
    [InlineData("jobs.my-job.outputs.count", "3")]
    [InlineData("'text'", "text")]
    [InlineData("'it''s'", "it's")]
    [InlineData("42", "42")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("true", "true")]
    [InlineData("trigger.branch == 'main'", "true")]
    [InlineData("trigger.branch != 'main'", "false")]
    [InlineData("trigger.branch == 'Main'", "false")]
    [InlineData("jobs.my-job.outputs.count == 3", "true")]
    [InlineData("vars.DEPLOY == true", "true")]
    [InlineData("vars.QUOTE == 'it''s'", "true")]
    [InlineData("!vars.DEPLOY", "false")]
    [InlineData("!!vars.DEPLOY", "true")]
    [InlineData("vars.DEPLOY && trigger.branch == 'main'", "true")]
    [InlineData("vars.EMPTY || vars.ZERO", "false")]
    [InlineData("vars.EMPTY || trigger.branch", "true")]
    [InlineData("!(vars.EMPTY || vars.ZERO)", "true")]
    [InlineData("false && true || true", "true")]
    [InlineData("false && (true || true)", "false")]
    [InlineData("vars.UNKNOWN == null", "true")]
    [InlineData("vars.NOTHING == null", "true")]
    [InlineData("vars.EMPTY == null", "false")]
    public void Expressions_evaluate_to_text(string text, string expected) => Evaluate(text).ShouldBe(expected);

    [Fact]
    public void Unknown_paths_and_null_evaluate_to_null()
    {
        Evaluate("vars.UNKNOWN").ShouldBeNull();
        Evaluate("null").ShouldBeNull();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("no", true)]
    [InlineData("00", true)]
    public void Truthiness_follows_the_documented_rules(string? value, bool expected) =>
        ExpressionEvaluator.IsTrue(value).ShouldBe(expected);

    [Fact]
    public void And_and_or_do_not_evaluate_the_right_side_needlessly()
    {
        var resolver = new CountingResolver();
        var (expression, _) = ExpressionSyntax.Parse("false && vars.A || true || vars.B");

        ExpressionEvaluator.EvaluateCondition(expression!, resolver).ShouldBeTrue();
        resolver.Lookups.ShouldBe(0);
    }

    [Theory]
    [InlineData("", 0, "empty")]
    [InlineData("   ", 0, "empty")]
    [InlineData("vars.A ==", 9, "ends too early")]
    [InlineData("vars.A == == 'b'", 10, "Expected a value")]
    [InlineData("vars.A 'b'", 7, "operator")]
    [InlineData("a == b == c", 7, "chained")]
    [InlineData("(vars.A", 0, "not closed")]
    [InlineData("vars.A)", 6, "operator")]
    [InlineData("'open", 0, "closing quote")]
    [InlineData("vars..A", 0, "not a valid path")]
    [InlineData("vars.A.", 0, "not a valid path")]
    [InlineData("vars.1st", 0, "not a valid path")]
    [InlineData("vars.A & vars.B", 7, "Unexpected character '&'")]
    [InlineData("vars.A = 'b'", 7, "Unexpected character '='")]
    [InlineData("$x", 0, "Unexpected character '$'")]
    public void Syntax_errors_say_what_and_where(string text, int offset, string message)
    {
        var (expression, error) = ExpressionSyntax.Parse(text);

        expression.ShouldBeNull();
        error.ShouldNotBeNull().Offset.ShouldBe(offset);
        error.Message.ShouldContain(message);
    }

    [Fact]
    public void Overly_long_expressions_are_rejected() =>
        ExpressionSyntax.Parse(new string('a', ExpressionSyntax.MaxLength + 1)).Error.ShouldNotBeNull().Message.ShouldContain("longer");

    [Fact]
    public void Overly_deep_nesting_is_rejected()
    {
        var text = new string('(', ExpressionSyntax.MaxDepth + 1) + "true" + new string(')', ExpressionSyntax.MaxDepth + 1);

        ExpressionSyntax.Parse(text).Error.ShouldNotBeNull().Message.ShouldContain("nested");
        ExpressionSyntax.Parse(new string('!', ExpressionSyntax.MaxDepth + 1) + "true").Error.ShouldNotBeNull();
    }

    [Fact]
    public void Paths_keep_their_segments_and_position()
    {
        var (expression, _) = ExpressionSyntax.Parse("  jobs.build.result");

        var path = expression.ShouldBeOfType<PathExpression>();
        path.Segments.ShouldBe(["jobs", "build", "result"]);
        path.Offset.ShouldBe(2);
        path.Text.ShouldBe("jobs.build.result");
    }

    [Theory]
    [InlineData("trigger.branch == 'main'")]
    [InlineData("${{ trigger.branch == 'main' }}")]
    [InlineData("  ${{trigger.branch == 'main'}}  ")]
    public void Conditions_may_be_wrapped_or_bare(string text)
    {
        var (expression, error) = ExpressionSyntax.ParseCondition(text);

        error.ShouldBeNull();
        ExpressionEvaluator.EvaluateCondition(expression!, Values).ShouldBeTrue();
    }

    [Theory]
    [InlineData("${{ vars.A }} && ${{ vars.B }}", 0)]
    [InlineData("vars.A && ${{ vars.B }}", 10)]
    public void Conditions_cannot_mix_bare_and_wrapped_parts(string text, int offset) =>
        ExpressionSyntax.ParseCondition(text).Error.ShouldNotBeNull().Offset.ShouldBe(offset);

    [Fact]
    public void Errors_in_wrapped_conditions_point_into_the_original_text() =>
        ExpressionSyntax.ParseCondition(" ${{ vars.A == }}").Error.ShouldNotBeNull().Offset.ShouldBe(15);

    [Fact]
    public void Arguments_must_not_be_null()
    {
        Should.Throw<ArgumentNullException>(() => ExpressionSyntax.Parse(null!));
        Should.Throw<ArgumentNullException>(() => ExpressionSyntax.ParseCondition(null!));
        Should.Throw<ArgumentNullException>(() => ExpressionEvaluator.Evaluate(null!, Values));
        Should.Throw<ArgumentNullException>(() => ExpressionEvaluator.Evaluate(new LiteralExpression("x"), null!));
    }

    private sealed class CountingResolver : IValueResolver
    {
        public int Lookups { get; private set; }

        public bool TryResolve(IReadOnlyList<string> path, out string? value)
        {
            Lookups++;
            value = "true";
            return true;
        }
    }
}
