using System.Net;
using AutoCanvas.Runner.Execution;
using AutoCanvas.Server.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoCanvas.Runner.Tests;

/// <summary>How the runner behaves around its jobs: failures, cancellation, lost connections, shutdown, credentials.</summary>
public sealed class RunnerLifecycleTests
{
    private const string Sleeping = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            runs_on: linux
            steps:
              - run: |
                  echo started
                  sleep 2
                  echo done
        """;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_failing_step_fails_the_job_and_the_rest_is_skipped()
    {
        await using var runner = await TestRunner.CreateAsync();
        await runner.RegisterAsync();
        await runner.StartAsync();

        var run = await runner.StartRunAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                steps:
                  - run: exit 3
                  - run: echo never
                reports:
                  - format: junit
                    path: missing/*.xml
            """);

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Failed");
        var attempt = (await runner.GetAsync($"/api/v1/runs/{run}/jobs/build")).GetProperty("attempts")[0];
        var steps = attempt.GetProperty("steps");
        steps[0].GetProperty("status").GetString().ShouldBe("Failed");
        steps[0].GetProperty("exitCode").GetInt32().ShouldBe(3);
        steps[1].GetProperty("status").GetString().ShouldBe("Skipped");
        attempt.GetProperty("warnings")[0].GetString()!.ShouldContain("no file matches missing/*.xml");
        ((FakeRuntime)runner.Runtime).Pulled.ShouldContain(i => i == "ubuntu:24.04");
    }

    [Fact]
    public async Task A_job_whose_image_cannot_be_pulled_fails_with_the_reason()
    {
        var runtime = new FakeRuntime();
        runtime.Missing.Add("nowhere/nothing:1");
        await using var runner = await TestRunner.CreateAsync(runtime);
        await runner.RegisterAsync();
        await runner.StartAsync();

        var run = await runner.StartRunAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                image: nowhere/nothing:1
                steps:
                  - run: echo never
            """);

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Failed");
        (await runner.LogAsync(run)).ShouldContain(l => l.Contains("pull access denied for nowhere/nothing:1", StringComparison.Ordinal));
        runtime.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_a_run_stops_the_running_step()
    {
        await using var runner = await TestRunner.CreateAsync();
        await runner.RegisterAsync();
        await runner.StartAsync();

        var run = await runner.StartRunAsync("""
            version: 1
            triggers:
              manual: true
            jobs:
              build:
                runs_on: linux
                steps:
                  - run: |
                      echo started
                      sleep-hard 60
            """);
        await TestRunner.EventuallyAsync(async () => (await runner.LogAsync(run)).Contains("started", StringComparer.Ordinal), "The step did not start.");
        (await runner.Admin.PostAsync(new Uri($"/api/v1/runs/{run}/cancel", UriKind.Relative), null, Cancel)).EnsureSuccessStatusCode();

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Cancelled");
        await TestRunner.EventuallyAsync(
            async () => (await runner.GetAsync($"/api/v1/runs/{run}/jobs/build")).GetProperty("attempts")[0].GetProperty("status").GetString() is not "Running",
            "The attempt did not end.");
        var step = (await runner.GetAsync($"/api/v1/runs/{run}/jobs/build")).GetProperty("attempts")[0].GetProperty("steps")[0];
        step.GetProperty("status").GetString().ShouldBe("Cancelled");
        ((FakeRuntime)runner.Runtime).Restarted.Count.ShouldBe(1);
        (await runner.LogAsync(run)).ShouldContain(l => l == "The job was cancelled; stopping the step.");
    }

    [Fact]
    public async Task A_job_goes_on_while_the_runner_reconnects()
    {
        await using var runner = await TestRunner.CreateAsync();
        await runner.RegisterAsync();
        await runner.StartAsync();

        var run = await runner.StartRunAsync(Sleeping);
        await TestRunner.EventuallyAsync(async () => (await runner.LogAsync(run)).Contains("started", StringComparer.Ordinal), "The step did not start.");
        await runner.Network.BreakAsync();

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Succeeded");
        runner.Network.Connects.ShouldBeGreaterThanOrEqualTo(2);
        (await runner.LogAsync(run)).ShouldContain(l => l == "done");
    }

    [Fact]
    public async Task A_stopping_runner_lets_its_job_finish()
    {
        await using var runner = await TestRunner.CreateAsync();
        await runner.RegisterAsync();
        var host = await runner.StartAsync();

        var run = await runner.StartRunAsync(Sleeping);
        await TestRunner.EventuallyAsync(async () => (await runner.LogAsync(run)).Contains("started", StringComparer.Ordinal), "The step did not start.");
        await host.StopAsync(Cancel);

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Succeeded");
    }

    [Fact]
    public async Task A_runner_stops_when_its_credential_is_revoked()
    {
        await using var runner = await TestRunner.CreateAsync();
        await runner.RegisterAsync();
        var credential = (await RunnerCredential.ReadAsync(Path.Combine(runner.DataDirectory, "credential.json"), Cancel))!;
        (await runner.Admin.DeleteAsync(new Uri($"/api/v1/runners/{credential.RunnerId}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var host = await runner.StartAsync();

        await TestRunner.EventuallyAsync(() => Task.FromResult(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested), "The runner did not stop.");
        host.Services.GetRequiredService<RunnerExitCode>().Value.ShouldBe(RunnerExitCode.NotAuthorized);
    }

    [Fact]
    public async Task A_runner_with_a_token_file_waits_for_the_token_registers_itself_and_runs_jobs()
    {
        await using var runner = await TestRunner.CreateAsync();
        var tokenFile = Path.Combine(runner.DataDirectory + "-handoff", "runner-token");
        await runner.StartAsync($"--Runner:RegistrationTokenFile={tokenFile}", "--Runner:Name=bundled");
        await Task.Delay(500, Cancel);
        File.Exists(Path.Combine(runner.DataDirectory, "credential.json")).ShouldBeFalse();

        // The setup hands the token over (ADR-0020).
        Directory.CreateDirectory(Path.GetDirectoryName(tokenFile)!);
        await File.WriteAllTextAsync(tokenFile, await runner.TokenAsync(), Cancel);

        await TestRunner.EventuallyAsync(
            async () => (await runner.GetAsync("/api/v1/runners")).GetProperty("items").EnumerateArray()
                .Any(r => string.Equals(r.GetProperty("name").GetString(), "bundled", StringComparison.Ordinal) && r.GetProperty("isOnline").GetBoolean()),
            "The runner did not register and come online.");
        var run = await runner.StartRunAsync(Sleeping);
        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Succeeded");
        Directory.Delete(Path.GetDirectoryName(tokenFile)!, recursive: true);
    }

    [Fact]
    public async Task A_runner_whose_token_file_is_refused_says_so_and_exits()
    {
        await using var runner = await TestRunner.CreateAsync();
        var tokenFile = runner.DataDirectory + "-token";
        await File.WriteAllTextAsync(tokenFile, "acrt_wrong", Cancel);

        var host = await runner.StartAsync($"--Runner:RegistrationTokenFile={tokenFile}");

        await TestRunner.EventuallyAsync(() => Task.FromResult(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested), "The runner did not stop.");
        host.Services.GetRequiredService<RunnerExitCode>().Value.ShouldBe(RunnerExitCode.NotAuthorized);
        File.Delete(tokenFile);
    }

    [Fact]
    public async Task An_unregistered_runner_says_so_and_exits()
    {
        await using var runner = await TestRunner.CreateAsync();

        var error = new StringWriter();
        (await RunnerCommandLine.RunAsync(runner.Settings(), new StringWriter(), error, runner.Configure)).ShouldBe(RunnerExitCode.NotAuthorized, error.ToString());
    }

    [Fact]
    public async Task Registering_needs_a_valid_token_settings_and_a_runner_not_registered_yet()
    {
        await using var runner = await TestRunner.CreateAsync();
        var error = new StringWriter();

        (await runner.RegisterAsync(token: "acrt_wrong", error: error)).ShouldBe(1);
        error.ToString().ShouldContain("The server refused the registration");
        (await runner.RegisterAsync()).ShouldBe(0);
        (await runner.RegisterAsync(error: error)).ShouldBe(1);
        error.ToString().ShouldContain("registered already");
        var invalid = new StringWriter();
        (await RunnerCommandLine.RunAsync(["register", "--token", "x", "--name", "y", "--Runner:Server=ftp://nowhere", $"--Runner:DataDirectory={runner.DataDirectory}"], new StringWriter(), invalid, runner.Configure)).ShouldBe(1);
        invalid.ToString().ShouldContain("Runner:Server");
    }

    [Fact]
    public async Task Leftovers_of_an_earlier_run_are_removed_on_start()
    {
        var runtime = new FakeRuntime { Leftovers = 2 };
        await using var runner = await TestRunner.CreateAsync(runtime);
        await runner.RegisterAsync();
        var leftover = Path.Combine(runner.DataDirectory, "work", Guid.NewGuid().ToString(), "workspace");
        Directory.CreateDirectory(leftover);
        await File.WriteAllTextAsync(Path.Combine(leftover, "file.txt"), "old", Cancel);

        await runner.StartAsync();

        await TestRunner.EventuallyAsync(() => Task.FromResult(!Directory.Exists(leftover)), "The leftover directory was not removed.");
    }
}
