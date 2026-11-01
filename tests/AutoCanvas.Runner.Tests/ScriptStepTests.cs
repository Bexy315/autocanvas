using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Connection;
using AutoCanvas.Runner.Docker;
using AutoCanvas.Runner.Execution;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;

namespace AutoCanvas.Runner.Tests;

/// <summary>One script step in a container of <see cref="FakeRuntime"/>, without a server.</summary>
public sealed class ScriptStepTests : IAsyncDisposable
{
    private readonly FakeRuntime runtime = new();
    private readonly Outbox outbox = new();
    private readonly RunnerOptions options = new() { DataDirectory = Path.Combine(Path.GetTempPath(), "autocanvas-step-tests", Guid.NewGuid().ToString("N")) };
    private readonly ServerChannel server = new(GrpcChannel.ForAddress("http://localhost:1"));

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_step_that_runs_too_long_is_stopped_and_timed_out()
    {
        var completed = await RunAsync(Script("sleep 30", timeout: TimeSpan.FromMilliseconds(200)), Cancel);

        completed.Outcome.ShouldBe(StepOutcome.TimedOut);
        completed.HasExitCode.ShouldBeFalse();
    }

    [Fact]
    public async Task A_step_that_ignores_the_signal_ends_with_a_restart_of_its_container()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var completed = await RunAsync(Script("sleep-hard 30"), cancel.Token);

        completed.Outcome.ShouldBe(StepOutcome.Cancelled);
        runtime.Restarted.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("zsh", "has no zsh")]
    [InlineData("pwsh", "has no pwsh")]
    public async Task A_shell_the_image_or_the_runner_lacks_is_an_error_of_the_runner(string shell, string message)
    {
        runtime.Shells.Remove("pwsh");

        var completed = await RunAsync(Script("echo hi", shell: shell), Cancel);

        completed.Outcome.ShouldBe(StepOutcome.Errored);
        completed.Error.ShouldContain(message);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/etc")]
    [InlineData("a/../../b")]
    [InlineData("C:/Windows")]
    public async Task A_working_directory_outside_the_workspace_is_refused(string directory)
    {
        var step = Script("echo hi");
        step.Script.WorkingDirectory = directory;

        var completed = await RunAsync(step, Cancel);

        completed.Outcome.ShouldBe(StepOutcome.Errored);
        completed.Error.ShouldContain("not a path inside the workspace");
    }

    [Fact]
    public async Task A_node_step_is_refused_until_the_runner_implements_node_types()
    {
        var completed = await RunAsync(new Step { Index = 0, Node = new NodeAction { Type = "docker_build" } }, Cancel);

        completed.Outcome.ShouldBe(StepOutcome.Errored);
        completed.Error.ShouldContain("node type 'docker_build'");
    }

    [Fact]
    public async Task Outputs_and_errors_are_masked_and_a_working_directory_is_created()
    {
        var step = Script("echo token=s3cret-value >> \"$AUTOCANVAS_OUTPUT\"");
        step.MaskValues.Add("s3cret-value");
        step.Script.WorkingDirectory = "src/./app";

        var completed = await RunAsync(step, Cancel);

        completed.Outcome.ShouldBe(StepOutcome.Succeeded);
        completed.ExitCode.ShouldBe(0);
        completed.Outputs["token"].ShouldBe("***");
        Directory.Exists(Path.Combine(options.WorkRoot, "job", "workspace", "src", "app")).ShouldBeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        server.Dispose();
        if (Directory.Exists(options.DataDirectory))
        {
            Directory.Delete(options.DataDirectory, recursive: true);
        }

        await Task.CompletedTask;
    }

    private static Step Script(string text, string shell = "bash", TimeSpan? timeout = null)
    {
        var step = new Step { Index = 0, DisplayName = "step", Script = new ScriptAction { Shell = shell, Script = text } };
        if (timeout is { } limit)
        {
            step.Timeout = Duration.FromTimeSpan(limit);
        }

        return step;
    }

    private async Task<StepCompleted> RunAsync(Step step, CancellationToken cancel)
    {
        var services = new JobServices(runtime, outbox, server, options, TimeProvider.System) { GracePeriod = TimeSpan.FromMilliseconds(100) };
        var paths = new JobPaths(options, "job");
        Directory.CreateDirectory(paths.Workspace);
        Directory.CreateDirectory(paths.Meta);
        var container = await runtime.StartAsync(new ContainerSpec("alpine", [], paths.Binds, new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal)), Cancel);
        var log = new LogBatcher("job", outbox, TimeProvider.System);
        await using (log)
        {
            return await new ScriptStep(services, paths, container, log).RunAsync(step, cancel, Cancel);
        }
    }
}
