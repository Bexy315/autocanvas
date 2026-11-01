using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Nodes;

namespace AutoCanvas.Core.Tests.Validation;

public sealed class NodeCheckTests
{
    private static IReadOnlyList<Diagnostic> Load(string with, string type = "http_request")
    {
        var steps = $"""
            version: 1
            jobs:
              call:
                steps:
                  - id: node
                    type: {type}
            {with}
            """;
        return AutomationLoader.Load(steps, BuiltInNodeTypes.All).Diagnostics;
    }

    [Theory]
    [InlineData("""
                with:
                  url: https://example.org
                  method: post
                  headers: { Accept: application/json, X-Count: 3 }
                  body: '{ "a": 1 }'
                  timeout_seconds: 10
                  fail_on_status: false
        """)]
    [InlineData("""
                with:
                  url: ${{ vars.URL }}
                  method: ${{ vars.METHOD }}
                  timeout_seconds: ${{ vars.WAIT }}
                  fail_on_status: ${{ vars.STRICT }}
        """)]
    public void Fitting_inputs_have_no_problems(string with) =>
        Load(with).ShouldBeEmpty();

    [Fact]
    public void A_docker_build_with_every_input_has_no_problems() =>
        Load(
            """
                    with:
                      image: ghcr.io/team/app
                      tags: [latest, '${{ run.commit_short_sha }}', 1.0]
                      context: src
                      dockerfile: build/Dockerfile
                      build_args: { VERSION: '1.2', DEBUG: true }
                      target: runtime
                      push: true
                      registry: ghcr.io
                      username: bot
                      password: ${{ secrets.TOKEN }}
            """,
            "docker_build").ShouldBeEmpty();

    [Theory]
    [InlineData("http_reqest", "", DiagnosticCodes.InvalidValue, "jobs.call.steps[0].type", "Did you mean 'http_request'?")]
    [InlineData("publisher/custom", "", DiagnosticCodes.InvalidValue, "jobs.call.steps[0].type", "Use one of: docker_build, http_request.")]
    [InlineData("http_request", "", DiagnosticCodes.MissingKey, "jobs.call.steps[0].with", "needs the input 'url'")]
    [InlineData("http_request", "        with: { url: x, retries: 3 }", DiagnosticCodes.UnknownKey, "jobs.call.steps[0].with.retries", "not an input of http_request")]
    [InlineData("http_request", "        with: { url: x, metod: GET }", DiagnosticCodes.UnknownKey, "jobs.call.steps[0].with.metod", "Did you mean 'method'?")]
    [InlineData("http_request", "        with: { url: x, method: FETCH }", DiagnosticCodes.InvalidValue, "jobs.call.steps[0].with.method", "Use one of: GET")]
    [InlineData("http_request", "        with: { url: [x] }", DiagnosticCodes.WrongType, "jobs.call.steps[0].with.url", "Expected a text")]
    [InlineData("http_request", "        with: { url: x, timeout_seconds: 2.5 }", DiagnosticCodes.WrongType, "jobs.call.steps[0].with.timeout_seconds", "a whole number")]
    [InlineData("http_request", "        with: { url: x, fail_on_status: 'yes' }", DiagnosticCodes.WrongType, "jobs.call.steps[0].with.fail_on_status", "true or false")]
    [InlineData("http_request", "        with: { url: x, headers: { Accept: [a] } }", DiagnosticCodes.WrongType, "jobs.call.steps[0].with.headers", "a mapping of names to texts")]
    [InlineData("docker_build", "        with: { image: app, tags: latest }", DiagnosticCodes.WrongType, "jobs.call.steps[0].with.tags", "a list of texts")]
    public void Unknown_types_and_unfitting_inputs_are_reported(string type, string with, string code, string path, string message)
    {
        var problem = Load(with, type).ShouldHaveSingleItem();

        problem.Code.ShouldBe(code);
        problem.Path.ShouldBe(path);
        problem.Message.ShouldContain(message);
    }

    [Fact]
    public void Without_a_list_of_node_types_any_node_type_passes() =>
        AutomationLoader.Load("""
            version: 1
            jobs:
              call:
                steps:
                  - type: publisher/custom
                    with: { anything: [1, 2] }
            """).Diagnostics.ShouldBeEmpty();

    [Fact]
    public void Every_built_in_node_type_describes_its_inputs_and_outputs()
    {
        foreach (var type in BuiltInNodeTypes.All.Values)
        {
            type.Description.ShouldNotBeNullOrWhiteSpace();
            type.Inputs.ShouldAllBe(i => i.Description.Length > 0);
            type.Outputs.ShouldNotBeEmpty();
        }
    }
}
