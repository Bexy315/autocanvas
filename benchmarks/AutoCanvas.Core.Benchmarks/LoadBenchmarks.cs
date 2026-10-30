using AutoCanvas.Core.Parsing;
using AutoCanvas.Core.Validation;
using BenchmarkDotNet.Attributes;

namespace AutoCanvas.Core.Benchmarks;

/// <summary>Reading automation files: parsing alone, and parsing plus validation.</summary>
[MemoryDiagnoser]
public class LoadBenchmarks
{
    private string _yaml = string.Empty;
    private ParseResult _parsed = null!;

    [Params(10, 100)]
    public int Jobs { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _yaml = SampleAutomation.Yaml(Jobs);
        if (!AutomationLoader.Load(_yaml).Succeeded)
        {
            throw new InvalidOperationException("The sample automation must be valid.");
        }

        _parsed = AutomationParser.Parse(_yaml);
    }

    [Benchmark(Baseline = true)]
    public ParseResult Parse() => AutomationParser.Parse(_yaml);

    [Benchmark]
    public ParseResult ParseAndValidate() => AutomationLoader.Load(_yaml);

    [Benchmark]
    public int ValidateOnly() => AutomationValidator.Validate(_parsed.Definition!, _parsed.Locations).Count;
}
