using System.Text.Json;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;
using static AutoCanvas.Core.Tests.Parsing.ParserAssertions;

namespace AutoCanvas.Core.Tests.Parsing;

public sealed class StepTests
{
    private static StepDefinition ParseStep(string step) => ParseJob("steps:\n" + Indent(step, "  ")).Steps.Single();

    [Fact]
    public void A_script_step_is_read()
    {
        var step = ParseStep("""
            - id: compile
              name: Compile
              if: ${{ vars.BUILD }}
              env: { LEVEL: 2 }
              continue_on_error: true
              timeout_minutes: 5
              run: |
                make all
              shell: bash
              working_directory: src
            """).ShouldBeOfType<ScriptStep>();

        step.Id.ShouldBe("compile");
        step.Name.ShouldBe("Compile");
        step.Condition.ShouldBe("${{ vars.BUILD }}");
        step.Env["LEVEL"].ShouldBe("2");
        step.ContinueOnError.ShouldBeTrue();
        step.TimeoutMinutes.ShouldBe(5);
        step.Run.ShouldBe("make all\n");
        step.Shell.ShouldBe(ScriptShell.Bash);
        step.WorkingDirectory.ShouldBe("src");
    }

    [Theory]
    [InlineData("sh", ScriptShell.Sh)]
    [InlineData("pwsh", ScriptShell.Pwsh)]
    [InlineData("cmd", ScriptShell.Cmd)]
    public void Shells_are_recognized(string shell, ScriptShell expected) =>
        ParseStep($"- run: x\n  shell: {shell}").ShouldBeOfType<ScriptStep>().Shell.ShouldBe(expected);

    [Fact]
    public void A_script_step_without_options_gets_defaults()
    {
        var step = ParseStep("- run: make").ShouldBeOfType<ScriptStep>();

        step.Id.ShouldBeNull();
        step.Shell.ShouldBeNull();
        step.Env.ShouldBeEmpty();
        step.ContinueOnError.ShouldBeFalse();
    }

    [Fact]
    public void A_node_step_keeps_its_inputs_as_json()
    {
        var step = ParseStep("""
            - id: call
              type: http_request
              with:
                method: POST
                url: https://example.org
                headers: { Accept: application/json }
                retries: 3
                ratio: 0.5
                verbose: false
                body: ~
                port: '8080'
                tags: [a, b]
              outputs: { status: '${{ steps.call.outputs.status }}' }
            """).ShouldBeOfType<NodeStep>();

        step.Type.ShouldBe("http_request");
        step.Outputs["status"].ShouldBe("${{ steps.call.outputs.status }}");
        var inputs = step.Inputs;
        inputs.GetProperty("method").GetString().ShouldBe("POST");
        inputs.GetProperty("headers").GetProperty("Accept").GetString().ShouldBe("application/json");
        inputs.GetProperty("retries").GetInt64().ShouldBe(3);
        inputs.GetProperty("ratio").GetDouble().ShouldBe(0.5);
        inputs.GetProperty("verbose").GetBoolean().ShouldBeFalse();
        inputs.GetProperty("body").ValueKind.ShouldBe(JsonValueKind.Null);
        inputs.GetProperty("port").GetString().ShouldBe("8080");
        inputs.GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void A_node_step_without_inputs_has_an_empty_object()
    {
        var step = ParseStep("- type: publisher/custom_node").ShouldBeOfType<NodeStep>();

        step.Type.ShouldBe("publisher/custom_node");
        step.Inputs.ValueKind.ShouldBe(JsonValueKind.Object);
        step.Inputs.EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public void Inputs_keep_numbers_beyond_integer_range_as_written() =>
        ParseStep("- type: node\n  with: { big: 123456789012345678901234567890, inf: .inf }")
            .ShouldBeOfType<NodeStep>().Inputs.GetProperty("inf").GetString().ShouldBe(".inf");

    [Theory]
    [InlineData("- make", DiagnosticCodes.WrongType, "jobs.build.steps[0]")]
    [InlineData("- name: nothing", DiagnosticCodes.ConflictingKeys, "jobs.build.steps[0]")]
    [InlineData("- run: a\n  type: http_request", DiagnosticCodes.ConflictingKeys, "jobs.build.steps[0]")]
    [InlineData("- run: ''", DiagnosticCodes.InvalidValue, "jobs.build.steps[0].run")]
    [InlineData("- run: a\n  shell: zsh", DiagnosticCodes.InvalidValue, "jobs.build.steps[0].shell")]
    [InlineData("- run: a\n  with: { x: 1 }", DiagnosticCodes.UnknownKey, "jobs.build.steps[0].with")]
    [InlineData("- run: a\n  id: Compile", DiagnosticCodes.InvalidValue, "jobs.build.steps[0].id")]
    [InlineData("- run: a\n  env: { 1X: a }", DiagnosticCodes.InvalidValue, "jobs.build.steps[0].env.1X")]
    [InlineData("- run: a\n  continue_on_error: maybe", DiagnosticCodes.WrongType, "jobs.build.steps[0].continue_on_error")]
    [InlineData("- run: a\n  timeout_minutes: 0", DiagnosticCodes.InvalidValue, "jobs.build.steps[0].timeout_minutes")]
    [InlineData("- type: HttpRequest", DiagnosticCodes.InvalidValue, "jobs.build.steps[0].type")]
    [InlineData("- type: 42", DiagnosticCodes.WrongType, "jobs.build.steps[0].type")]
    [InlineData("- type: node\n  with: [a]", DiagnosticCodes.WrongType, "jobs.build.steps[0].with")]
    [InlineData("- type: node\n  outputs: { Bad: x }", DiagnosticCodes.InvalidValue, "jobs.build.steps[0].outputs.Bad")]
    [InlineData("- type: node\n  shell: bash", DiagnosticCodes.UnknownKey, "jobs.build.steps[0].shell")]
    [InlineData("- type: node\n  config: { a: 1 }", DiagnosticCodes.UnknownKey, "jobs.build.steps[0].config")]
    public void Invalid_steps_are_reported(string step, string code, string path) =>
        SingleError(WithJob("steps:\n" + Indent(step, "  ")), code, path);

    [Fact]
    public void Diagnostics_point_at_the_offending_line()
    {
        var diagnostic = SingleError(WithJob("steps:\n  - run: a\n    shell: zsh"), DiagnosticCodes.InvalidValue, "jobs.build.steps[0].shell");

        diagnostic.Location.ShouldBe(new SourceLocation(6, 16));
        diagnostic.ToString().ShouldBe("(6:16) AC0104 jobs.build.steps[0].shell: 'zsh' is not allowed here. Use one of: bash, sh, pwsh, cmd.");
    }
}
