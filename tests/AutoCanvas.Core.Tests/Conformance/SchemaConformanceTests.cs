using AutoCanvas.Core.Parsing;
using Corvus.Json;
using Corvus.Json.Validator;

namespace AutoCanvas.Core.Tests.Conformance;

/// <summary>
/// The JSON Schema and the parser describe the same format. Every example in schema/examples must
/// be accepted by both, every example in schema/examples/invalid rejected by both.
/// </summary>
public sealed class SchemaConformanceTests
{
    private static readonly string SchemaDirectory = Path.Combine(AppContext.BaseDirectory, "schema");

    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromFile(Path.Combine(SchemaDirectory, "automation.v1.json")));

    public static TheoryData<string> ValidExamples() => Examples(Path.Combine(SchemaDirectory, "examples"));

    public static TheoryData<string> InvalidExamples() => Examples(Path.Combine(SchemaDirectory, "examples", "invalid"));

    [Theory]
    [MemberData(nameof(ValidExamples))]
    public void Valid_examples_satisfy_the_schema(string file)
    {
        var result = Schema.Value.Validate(YamlAsJson.Load(Read(file)), ValidationLevel.Detailed);

        result.IsValid.ShouldBeTrue(string.Join(Environment.NewLine, result.Results.Where(r => !r.Valid).Select(r => $"{r.Location}: {r.Message}")));
    }

    [Theory]
    [MemberData(nameof(ValidExamples))]
    public void Valid_examples_parse_without_diagnostics(string file)
    {
        var result = AutomationParser.Parse(Read(file));

        result.Diagnostics.ShouldBeEmpty();
        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidExamples))]
    public void Invalid_examples_violate_the_schema(string file) =>
        Schema.Value.Validate(YamlAsJson.Load(Read(file)), ValidationLevel.Flag).IsValid.ShouldBeFalse();

    [Theory]
    [MemberData(nameof(InvalidExamples))]
    public void Invalid_examples_are_rejected_by_the_parser(string file)
    {
        var result = AutomationParser.Parse(Read(file));

        result.Succeeded.ShouldBeFalse();
        result.Diagnostics.ShouldNotBeEmpty();
    }

    [Fact]
    public void There_are_examples_of_both_kinds()
    {
        ValidExamples().Count.ShouldBeGreaterThanOrEqualTo(2);
        InvalidExamples().Count.ShouldBeGreaterThanOrEqualTo(9);
    }

    private static TheoryData<string> Examples(string directory)
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.yml").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(SchemaDirectory, file).Replace('\\', '/'));
        }

        return data;
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(SchemaDirectory, file));
}
