namespace AutoCanvas.Runner.Tests;

/// <summary>A job's services and checkouts, through the server, with containers played by <see cref="FakeRuntime"/>.</summary>
public sealed class JobEnvironmentTests
{
    [Fact]
    public async Task Services_run_on_a_network_of_the_job_and_checkouts_are_cloned_before_the_steps()
    {
        var runtime = new FakeRuntime();
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
                checkout:
                  - repository: https://example.org/tools.git
                    ref: v2
                    path: tools
                  - repository: https://example.org/app.git
                    ref: 0123456789abcdef0123456789abcdef01234567
                    path: app
                    depth: 0
                services:
                  db:
                    preset: postgres
                steps:
                  - run: echo ready
            """);

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Succeeded");
        var network = runtime.Networks.ShouldHaveSingleItem();
        var service = runtime.Started.Values.Single(c => c.Image.StartsWith("postgres:", StringComparison.Ordinal));
        service.NetworkAlias.ShouldBe("db");
        service.Network.ShouldBe("network-" + network);
        service.Env["POSTGRES_PASSWORD"].ShouldBe("postgres");
        runtime.Started.Values.Single(c => string.Equals(c.Image, "ubuntu:24.04", StringComparison.Ordinal)).Network.ShouldBe(service.Network);
        runtime.Started.Values.Single(c => string.Equals(c.Image, "alpine/git:v2.52.0", StringComparison.Ordinal)).Entrypoint.ShouldBe(["sh"]);
        var helpers = runtime.Helpers.Select(h => string.Join(' ', h.Command)).ToList();
        helpers.ShouldContain(h => h == "git clone --depth 1 --branch v2 -- https://example.org/tools.git /workspace/tools");
        helpers.ShouldContain(h => h == "git -C /workspace/app fetch --no-tags origin 0123456789abcdef0123456789abcdef01234567");
        helpers.ShouldContain(h => h.Contains("pg_isready", StringComparison.Ordinal));
        runtime.RemovedNetworks.ShouldHaveSingleItem().ShouldBe(service.Network);
        runtime.Removed.Count.ShouldBe(3);
        var log = await runner.LogAsync(run);
        log.ShouldContain(l => l == "Service db is ready.");
        log.ShouldContain(l => l == "Checking out https://example.org/tools.git at v2 into /workspace/tools.");
    }

    [Fact]
    public async Task A_service_that_stops_fails_the_job_with_its_last_lines()
    {
        var runtime = new FakeRuntime();
        runtime.Crashing.Add("valkey/valkey:8");
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
                services:
                  cache:
                    image: valkey/valkey:8
                    ready: valkey-cli ping
                steps:
                  - run: echo never
            """);

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Failed");
        var log = await runner.LogAsync(run);
        log.ShouldContain(l => l == "Service cache stopped before it got ready.");
        log.ShouldContain(l => l == "[cache] valkey/valkey:8 says goodbye");
    }

    [Fact]
    public async Task A_checkout_that_fails_fails_the_job()
    {
        var runtime = new FakeRuntime();
        runtime.Failing.Add("git");
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
                checkout:
                  - repository: https://example.org/missing.git
                steps:
                  - run: echo never
            """);

        (await runner.EndedRunAsync(run)).GetProperty("state").GetString().ShouldBe("Failed");
        var lines = await runner.LogAsync(run);
        lines.ShouldContain(l => l == "Checking out https://example.org/missing.git failed: git clone exited with 128.", string.Join(" | ", lines));
    }
}
