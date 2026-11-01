using System.Net.Http.Json;
using AutoCanvas.Server.Tests;

namespace AutoCanvas.Runner.Tests;

/// <summary>The runner against the real server, with containers played by <see cref="FakeRuntime"/>.</summary>
public sealed class RunnerTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_registered_runner_runs_a_job_step_by_step_with_outputs_logs_and_reports()
    {
        await using var runner = await TestRunner.CreateAsync();
        var output = new StringWriter();
        (await runner.RegisterAsync(output: output)).ShouldBe(0);
        output.ToString().ShouldContain("Registered runner 'runner-1'");
        var folder = (await (await runner.Admin.PostAsJsonAsync("/api/v1/folders", new { name = "Team" }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        (await runner.Admin.PutAsJsonAsync($"/api/v1/folders/{folder}/secrets/TOKEN", new { value = "s3cret-token" }, Cancel)).EnsureSuccessStatusCode();
        await runner.StartAsync();

        var run = await runner.StartRunAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                image: alpine:3.22
                env:
                  GREETING: hello
                outputs:
                  version: ${{ steps.first.outputs.version }}
                steps:
                  - id: first
                    run: |
                      echo $GREETING from the job
                      echo version=1.2.3 >> "$AUTOCANVAS_OUTPUT"
                  - id: second
                    env:
                      TOKEN: ${{ secrets.TOKEN }}
                    run: |
                      echo the token is $TOKEN
                      echo something went wrong >&2
                      write reports/junit.xml <testsuite><testcase classname="C" name="a"/></testsuite>
                reports:
                  - format: junit
                    path: reports/*.xml
            """, folder);

        var ended = await runner.EndedRunAsync(run);
        ended.GetProperty("state").GetString().ShouldBe("Succeeded");
        var job = await runner.GetAsync($"/api/v1/runs/{run}/jobs/build");
        job.GetProperty("outputs").GetProperty("version").GetString().ShouldBe("1.2.3");
        var attempt = job.GetProperty("attempts")[0];
        attempt.GetProperty("runnerName").GetString().ShouldBe("runner-1");
        attempt.GetProperty("steps")[0].GetProperty("outputs").GetProperty("version").GetString().ShouldBe("1.2.3");
        attempt.GetProperty("reports")[0].GetProperty("tests").GetProperty("total").GetInt32().ShouldBe(1);
        attempt.GetProperty("warnings").GetArrayLength().ShouldBe(0);
        var log = await runner.LogAsync(run);
        log.ShouldContain(l => l == "hello from the job", string.Join(" | ", log));
        log.ShouldContain(l => l == "the token is ***");
        log.ShouldContain(l => l == "something went wrong");
        log.ShouldContain(l => l == "Started the job's container from alpine:3.22.");
        var runtime = (FakeRuntime)runner.Runtime;
        runtime.Pulled.ShouldContain(i => i == "alpine:3.22");
        runtime.Removed.Count.ShouldBe(1);
        var container = runtime.Started.Values.ShouldHaveSingleItem();
        container.Env["GREETING"].ShouldBe("hello");
        container.Labels["autocanvas.job"].ShouldBe(attempt.GetProperty("id").GetString());
        Directory.EnumerateDirectories(Path.Combine(runner.DataDirectory, "work")).ShouldBeEmpty();
    }
}
