using AutoCanvas.Core.Expressions;
using BenchmarkDotNet.Attributes;

namespace AutoCanvas.Core.Benchmarks;

/// <summary>The expression language on its hot paths: parsing, conditions and template rendering.</summary>
[MemoryDiagnoser]
public class ExpressionBenchmarks
{
    private const string ConditionText = "jobs.build.outputs.ok == 'true' && (trigger.branch == 'main' || !vars.DRY_RUN)";
    private const string TemplateText = "docker build -t app:${{ run.commit_short_sha }} --build-arg MODE=${{ vars.MODE }} .";

    private readonly Values _values = new();
    private Expression _condition = null!;
    private Template _template = null!;

    [GlobalSetup]
    public void Setup()
    {
        _condition = ExpressionSyntax.ParseCondition(ConditionText).Expression!;
        _template = Template.Parse(TemplateText).Template;
    }

    [Benchmark]
    public Expression? ParseCondition() => ExpressionSyntax.ParseCondition(ConditionText).Expression;

    [Benchmark]
    public bool EvaluateCondition() => ExpressionEvaluator.EvaluateCondition(_condition, _values);

    [Benchmark]
    public string RenderTemplate() => _template.Render(_values);

    [Benchmark]
    public string ParseAndRenderTemplate() => Template.Parse(TemplateText).Template.Render(_values);

    private sealed class Values : IValueResolver
    {
        public bool TryResolve(IReadOnlyList<string> path, out string? value)
        {
            value = path[^1] switch
            {
                "ok" => "true",
                "branch" => "main",
                "commit_short_sha" => "abc1234",
                _ => null,
            };
            return value is not null;
        }
    }
}
