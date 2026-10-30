using System.Text;
using System.Text.Json;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using AutoCanvas.Core.Parsing;
using static AutoCanvas.Core.Tests.Parsing.ParserAssertions;

namespace AutoCanvas.Core.Tests.Parsing;

public sealed class DocumentTests
{
    [Fact]
    public void A_minimal_automation_gets_the_defaults()
    {
        var automation = ParseValid(MinimalJob);

        automation.Version.ShouldBe(1);
        automation.Name.ShouldBeNull();
        automation.Triggers.ShouldBe(Triggers.None);
        automation.Variables.ShouldBeEmpty();
        automation.Concurrency.ShouldBe(Concurrency.Parallel);
        automation.Extensions.ShouldBeEmpty();
        automation.Jobs.ShouldHaveSingleItem().Id.ShouldBe("build");
    }

    [Fact]
    public void Top_level_settings_are_read()
    {
        var automation = ParseValid("""
            version: 1
            name: Nightly
            description: Runs at night.
            variables:
              REGION: eu
              RETRIES: 3
              DRY_RUN: false
            timeout_minutes: 30
            concurrency: single
            changed_when: ${{ jobs.build.outputs.changed }}
            jobs:
              build:
                steps:
                  - run: make
            x-editor:
              layout:
                build: { x: 10, y: 20.5 }
            """);

        automation.Name.ShouldBe("Nightly");
        automation.Description.ShouldBe("Runs at night.");
        automation.Variables.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal) { ["REGION"] = "eu", ["RETRIES"] = "3", ["DRY_RUN"] = "false" });
        automation.TimeoutMinutes.ShouldBe(30);
        automation.Concurrency.ShouldBe(Concurrency.Exclusive);
        automation.ChangedWhen.ShouldBe("${{ jobs.build.outputs.changed }}");
        var layout = automation.Extensions["x-editor"].GetProperty("layout").GetProperty("build");
        layout.GetProperty("x").GetInt64().ShouldBe(10);
        layout.GetProperty("y").GetDouble().ShouldBe(20.5);
    }

    [Fact]
    public void Jobs_keep_file_order()
    {
        var automation = ParseValid("""
            version: 1
            jobs:
              zeta: { steps: [ { run: a } ] }
              alpha: { steps: [ { run: b } ] }
              mid: { steps: [ { run: c } ] }
            """);

        automation.Jobs.Select(j => j.Id).ShouldBe(["zeta", "alpha", "mid"]);
    }

    [Theory]
    [InlineData("name: on")]
    [InlineData("name: yes")]
    [InlineData("name: off")]
    public void YAML_1_1_booleans_stay_text(string line) =>
        ParseValid(line + "\n" + MinimalJob).Name.ShouldBe(line[6..]);

    [Fact]
    public void Invalid_yaml_is_reported_with_its_location()
    {
        var diagnostic = SingleError("version: 1\njobs: [unclosed\n", DiagnosticCodes.InvalidYaml, string.Empty);

        diagnostic.Message.ShouldContain("not valid YAML");
    }

    [Fact]
    public void Duplicate_keys_are_invalid_yaml() =>
        SingleError("version: 1\nversion: 1\n" + MinimalJob[11..], DiagnosticCodes.InvalidYaml, string.Empty);

    [Theory]
    [InlineData("")]
    [InlineData("version: 1\n---\nversion: 1\n")]
    public void A_file_must_hold_exactly_one_document(string yaml) =>
        SingleError(yaml, DiagnosticCodes.NotOneDocument, string.Empty);

    [Fact]
    public void The_root_must_be_a_mapping() =>
        SingleError("- just\n- a list\n", DiagnosticCodes.WrongType, string.Empty);

    [Fact]
    public void Oversized_files_are_rejected_before_parsing() =>
        SingleError(new string('#', AutomationParser.MaxLength + 1), DiagnosticCodes.TooLarge, string.Empty);

    [Fact]
    public void Deep_nesting_is_rejected()
    {
        var yaml = MinimalJob + "\nx-deep: " + new string('[', AutomationParser.MaxDepth + 1) + new string(']', AutomationParser.MaxDepth + 1);

        SingleError(yaml, DiagnosticCodes.TooLarge, string.Empty);
    }

    [Fact]
    public void Alias_expansion_bombs_are_rejected()
    {
        var yaml = new StringBuilder(MinimalJob).Append("\nx-a0: &a0 [x, x, x, x, x, x, x, x, x, x]\n");
        for (var i = 1; i < 8; i++)
        {
            yaml.Append(System.Globalization.CultureInfo.InvariantCulture, $"x-a{i}: &a{i} [*a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}, *a{i - 1}]\n");
        }

        SingleError(yaml.ToString(), DiagnosticCodes.TooLarge, string.Empty);
    }

    [Fact]
    public void Version_is_required() =>
        SingleError("jobs:\n  build:\n    steps:\n      - run: make\n", DiagnosticCodes.MissingKey, string.Empty);

    [Fact]
    public void Unknown_versions_are_rejected()
    {
        var diagnostic = SingleError(MinimalJob.Replace("version: 1", "version: 2", StringComparison.Ordinal), DiagnosticCodes.UnsupportedVersion, "version");

        diagnostic.Location.ShouldBe(new SourceLocation(1, 10));
    }

    [Fact]
    public void A_quoted_version_is_text_not_a_number() =>
        SingleError(MinimalJob.Replace("version: 1", "version: '1'", StringComparison.Ordinal), DiagnosticCodes.WrongType, "version");

    [Fact]
    public void Jobs_are_required() =>
        SingleError("version: 1\n", DiagnosticCodes.MissingKey, string.Empty);

    [Fact]
    public void At_least_one_job_is_required() =>
        SingleError("version: 1\njobs: {}\n", DiagnosticCodes.InvalidValue, "jobs");

    [Fact]
    public void Text_values_must_not_be_numbers()
    {
        var diagnostic = SingleError("name: 42\n" + MinimalJob, DiagnosticCodes.WrongType, "name");

        diagnostic.Message.ShouldContain("quotes");
    }

    [Fact]
    public void Names_are_limited_in_length() =>
        SingleError($"name: {new string('n', 101)}\n" + MinimalJob, DiagnosticCodes.InvalidValue, "name");

    [Theory]
    [InlineData("concurrency: sometimes", "concurrency")]
    [InlineData("timeout_minutes: 0", "timeout_minutes")]
    [InlineData("timeout_minutes: 10081", "timeout_minutes")]
    [InlineData("timeout_minutes: soon", "timeout_minutes")]
    [InlineData("changed_when: ''", "changed_when")]
    [InlineData("description: [a]", "description")]
    [InlineData("variables: [a]", "variables")]
    [InlineData("variables: { 1ST: a }", "variables.1ST")]
    [InlineData("variables: { NAME: [a] }", "variables.NAME")]
    [InlineData("variables: { NAME: }", "variables.NAME")]
    public void Invalid_top_level_values_are_reported(string line, string path) =>
        AutomationParser.Parse(line + "\n" + MinimalJob).Diagnostics.ShouldHaveSingleItem().Path.ShouldBe(path);

    [Fact]
    public void Unknown_keys_are_errors_with_a_suggestion()
    {
        var diagnostic = SingleError("descripton: typo\n" + MinimalJob, DiagnosticCodes.UnknownKey, "descripton");

        diagnostic.Message.ShouldContain("Did you mean 'description'?");
        diagnostic.Location.ShouldBe(new SourceLocation(1, 1));
    }

    [Fact]
    public void Keys_of_the_origin_format_point_to_their_replacement()
    {
        var diagnostic = SingleError("trigger:\n  manual: true\n" + MinimalJob, DiagnosticCodes.UnknownKey, "trigger");

        diagnostic.Message.ShouldContain("Use 'triggers' instead.");
    }

    [Fact]
    public void Extension_keys_are_kept_as_json()
    {
        var automation = ParseValid(MinimalJob + "\nx-meta: { owner: ops, tags: [a, 'b'], weight: 1.5, active: true, note: ~, quoted: '7' }\n");

        var meta = automation.Extensions["x-meta"];
        meta.GetProperty("owner").GetString().ShouldBe("ops");
        meta.GetProperty("tags").GetArrayLength().ShouldBe(2);
        meta.GetProperty("weight").GetDouble().ShouldBe(1.5);
        meta.GetProperty("active").GetBoolean().ShouldBeTrue();
        meta.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        meta.GetProperty("quoted").GetString().ShouldBe("7");
    }

    [Fact]
    public void Several_problems_are_all_reported()
    {
        var result = AutomationParser.Parse("version: 1\nname: 5\nconcurrency: maybe\njobs:\n  build:\n    steps: []\n");

        result.Diagnostics.Select(d => d.Path).ShouldBe(["name", "concurrency", "jobs.build.steps"], ignoreOrder: true);
    }
}
