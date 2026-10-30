using AutoCanvas.Core.Expressions;
using AutoCanvas.Core.Model;
using AutoCanvas.Core.Orchestration;
using BenchmarkDotNet.Attributes;

namespace AutoCanvas.Core.Benchmarks;

/// <summary>
/// Deciding the next actions of a run. The server calls this every time a job finishes, so it
/// runs once per job of every run.
/// </summary>
[MemoryDiagnoser]
public class PlannerBenchmarks
{
    private readonly Values _values = new();
    private AutomationDefinition _automation = null!;
    private RunState _halfDone = null!;

    [Params(10, 100)]
    public int Jobs { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _automation = AutomationLoader.Load(SampleAutomation.Yaml(Jobs)).Definition!;
        var done = _automation.Jobs.Take(Jobs / 2).ToDictionary(j => j.Id, _ => new JobState(JobStatus.Succeeded), StringComparer.Ordinal);
        _halfDone = new RunState(done);
    }

    [Benchmark]
    public int PlanHalfDoneRun() => RunPlanner.Plan(_automation, _halfDone, _values).Count;

    private sealed class Values : IValueResolver
    {
        public bool TryResolve(IReadOnlyList<string> path, out string? value)
        {
            value = path[^1] is "ok" ? "true" : "main";
            return true;
        }
    }
}
