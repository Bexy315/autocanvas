using AutoCanvas.Runner.Docker;
using AutoCanvas.Runner.Execution;
using Docker.DotNet;

namespace AutoCanvas.Runner.Tests;

/// <summary>
/// The runner on a real Docker engine. Without one these tests are skipped — unless
/// <c>AUTOCANVAS_REQUIRE_DOCKER=1</c> (CI), where they fail.
/// </summary>
public sealed class DockerTests
{
    public const string RequireVariable = "AUTOCANVAS_REQUIRE_DOCKER";

    private const string Image = "alpine:3.22";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Containers_run_commands_and_stream_their_output()
    {
        using var docker = await DockerAsync();
        var id = await docker.StartAsync(
            new ContainerSpec(Image, ["sh", "-c", "trap 'exit 0' TERM; while :; do sleep 3600 & wait $!; done"], [], new Dictionary<string, string>(StringComparer.Ordinal) { ["FROM_CONTAINER"] = "yes" }, Labels()),
            Cancel);
        try
        {
            var lines = new List<OutputLine>();
            var exitCode = await docker.ExecAsync(
                id,
                new ExecSpec(["sh", "-c", "echo one; printf 'two'; echo err >&2; echo \"$FROM_CONTAINER $FROM_EXEC $(pwd)\"; exit 3"], "/tmp", new Dictionary<string, string>(StringComparer.Ordinal) { ["FROM_EXEC"] = "too" }),
                line =>
                {
                    lines.Add(line);
                    return Task.CompletedTask;
                },
                Cancel);

            exitCode.ShouldBe(3);
            lines.Where(l => !l.IsError).Select(l => l.Text).ShouldBe(["one", "twoyes too /tmp"]);
            lines.ShouldContain(l => l.IsError && l.Text == "err");
            await docker.RestartAsync(id, Cancel);
            (await docker.ExecAsync(id, new ExecSpec(["true"], "/", new Dictionary<string, string>(StringComparer.Ordinal)), _ => Task.CompletedTask, Cancel)).ShouldBe(0);
        }
        finally
        {
            (await docker.RemoveLabelledAsync(ContainerLabels.Job, "docker-test", Cancel)).ShouldBe(1);
            await docker.RemoveAsync(id, Cancel);
        }
    }

    [Fact]
    public async Task A_job_runs_in_a_real_container_and_leaves_nothing_behind()
    {
        using var docker = await DockerAsync();
        await using var runner = await TestRunner.CreateAsync(docker);
        await runner.RegisterAsync();
        await runner.StartAsync();

        var run = await runner.StartRunAsync($$"""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                image: {{Image}}
                steps:
                  - id: info
                    shell: sh
                    run: |
                      echo "running as $(id -u) in $(pwd)"
                      echo "release=$(cut -d. -f1,2 /etc/alpine-release)" >> "$AUTOCANVAS_OUTPUT"
                      mkdir -p reports
                      printf '<testsuite><testcase classname="C" name="a"/></testsuite>' > reports/junit.xml
                reports:
                  - format: junit
                    path: reports/*.xml
            """);

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Succeeded");
        var attempt = (await runner.GetAsync($"/api/v1/runs/{run}/jobs/build")).GetProperty("attempts")[0];
        attempt.GetProperty("steps")[0].GetProperty("outputs").GetProperty("release").GetString().ShouldBe("3.22");
        attempt.GetProperty("reports")[0].GetProperty("tests").GetProperty("total").GetInt32().ShouldBe(1);
        (await runner.LogAsync(run)).ShouldContain(l => l == "running as 0 in /workspace");
        Directory.EnumerateDirectories(Path.Combine(runner.DataDirectory, "work")).ShouldBeEmpty();
        (await docker.RemoveLabelledAsync(ContainerLabels.Job, attempt.GetProperty("id").GetString()!, Cancel)).ShouldBe(0);
    }

    [Fact]
    public async Task A_cancelled_step_in_a_real_container_is_stopped()
    {
        using var docker = await DockerAsync();
        await using var runner = await TestRunner.CreateAsync(docker);
        await runner.RegisterAsync();
        await runner.StartAsync();

        var run = await runner.StartRunAsync($$"""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                image: {{Image}}
                steps:
                  - shell: sh
                    run: |
                      echo started
                      sleep 120
            """);
        await TestRunner.EventuallyAsync(async () => (await runner.LogAsync(run)).Contains("started", StringComparer.Ordinal), "The step did not start.");
        (await runner.Admin.PostAsync(new Uri($"/api/v1/runs/{run}/cancel", UriKind.Relative), null, Cancel)).EnsureSuccessStatusCode();

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Cancelled");
        await TestRunner.EventuallyAsync(() => Task.FromResult(!Directory.EnumerateDirectories(Path.Combine(runner.DataDirectory, "work")).Any()), "The job's directory was not removed.");
    }

    private static Dictionary<string, string> Labels() => new(StringComparer.Ordinal) { [ContainerLabels.Managed] = "true", [ContainerLabels.Job] = "docker-test" };

    /// <summary>A runtime on the local Docker engine with the test image pulled, or a skipped test.</summary>
    private static async Task<DockerRuntime> DockerAsync()
    {
        var client = new DockerClientBuilder().Build();
        try
        {
            await client.System.PingAsync(Cancel);
        }
        catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException or TimeoutException)
        {
            client.Dispose();
            var reason = $"Docker is not available: {exception.Message}";
            if (string.Equals(Environment.GetEnvironmentVariable(RequireVariable), "1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(reason, exception);
            }

            Assert.Skip(reason);
            throw;
        }

        var runtime = new DockerRuntime(client);
        await runtime.PullAsync(Image, _ => Task.CompletedTask, Cancel);
        return runtime;
    }
}
