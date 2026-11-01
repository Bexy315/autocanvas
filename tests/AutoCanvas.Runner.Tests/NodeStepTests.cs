using System.Net;
using System.Text;
using System.Text.Json;
using AutoCanvas.Protocol.Runner.V1;
using AutoCanvas.Runner.Connection;
using AutoCanvas.Runner.Docker;
using AutoCanvas.Runner.Execution;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;

namespace AutoCanvas.Runner.Tests;

/// <summary>Node steps on their own: <c>http_request</c> against a stub server, <c>docker_build</c> in <see cref="FakeRuntime"/>.</summary>
public sealed class NodeStepTests : IAsyncDisposable
{
    private readonly FakeRuntime runtime = new();
    private readonly Outbox outbox = new();
    private readonly RunnerOptions options = new() { DataDirectory = Path.Combine(Path.GetTempPath(), "autocanvas-node-tests", Guid.NewGuid().ToString("N")) };
    private readonly ServerChannel server = new(GrpcChannel.ForAddress("http://localhost:1"));
    private static readonly string[] Tags = ["1.0", "latest"];
    private readonly StubHandler web = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_request_sends_its_method_headers_and_body_and_returns_status_and_body()
    {
        web.Answer = _ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("made it") };

        var completed = await RunAsync(Node("http_request", new { url = "https://api.example.org/items", method = "post", headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["Authorization"] = "Bearer s3cret-token", ["Content-Type"] = "application/json" }, body = "{\"a\":1}" }, "s3cret-token"), Cancel);

        completed.Outcome.ShouldBe(StepOutcome.Succeeded);
        completed.Outputs["status"].ShouldBe("201");
        completed.Outputs["body"].ShouldBe("made it");
        var sent = web.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe("POST");
        sent.Uri.ShouldBe("https://api.example.org/items");
        sent.Headers.ShouldContain(h => h == "Authorization: Bearer s3cret-token");
        sent.Headers.ShouldContain(h => h == "Content-Type: application/json");
        sent.Body.ShouldBe("{\"a\":1}");
        Logged().ShouldContain(l => l.StartsWith("POST https://api.example.org/items answered 201 Created", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_error_status_fails_the_step_unless_told_otherwise()
    {
        web.Answer = _ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("gone") };

        var failed = await RunAsync(Node("http_request", new { url = "http://example.org/x" }), Cancel);
        var tolerated = await RunAsync(Node("http_request", new { url = "http://example.org/x", fail_on_status = "false" }), Cancel);

        failed.Outcome.ShouldBe(StepOutcome.Failed);
        failed.Error.ShouldBe("The server answered 404 Not Found.");
        failed.Outputs["status"].ShouldBe("404");
        tolerated.Outcome.ShouldBe(StepOutcome.Succeeded);
    }

    [Fact]
    public async Task A_long_body_is_cut_and_a_slow_server_times_out()
    {
        web.Answer = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', HttpRequestNode.MaxBody + 10)) };
        var long_ = await RunAsync(Node("http_request", new { url = "https://example.org" }), Cancel);
        web.Answer = _ => throw new TaskCanceledException();
        web.Delay = TimeSpan.FromSeconds(5);

        var slow = await RunAsync(Node("http_request", new { url = "https://example.org", timeout_seconds = 1 }), Cancel);

        long_.Outputs["body"].Length.ShouldBe(HttpRequestNode.MaxBody);
        Logged().ShouldContain(l => l.Contains("longer than 32 KiB", StringComparison.Ordinal));
        slow.Outcome.ShouldBe(StepOutcome.Failed);
        slow.Error.ShouldContain("got no answer within 1 seconds");
    }

    [Fact]
    public async Task A_request_that_cannot_be_sent_fails_the_step()
    {
        web.Answer = _ => throw new HttpRequestException("No such host is known.");

        var completed = await RunAsync(Node("http_request", new { url = "https://nowhere.invalid" }), Cancel);

        completed.Outcome.ShouldBe(StepOutcome.Failed);
        completed.Error.ShouldBe("GET https://nowhere.invalid/ failed: No such host is known.");
    }

    [Theory]
    [InlineData("{\"url\":\"ftp://example.org\"}", "must be an http or https address")]
    [InlineData("{\"url\":\"https://example.org\",\"method\":\"FETCH\"}", "must be one of GET")]
    [InlineData("{\"url\":\"https://example.org\",\"timeout_seconds\":0}", "from 1 to 600")]
    [InlineData("{\"url\":\"https://example.org\",\"fail_on_status\":\"perhaps\"}", "true or false")]
    [InlineData("{\"url\":\"https://example.org\",\"headers\":\"x\"}", "mapping of names to texts")]
    [InlineData("{}", "'url' must be set")]
    public async Task Inputs_that_do_not_fit_fail_the_step(string inputs, string message)
    {
        var completed = await RunAsync(new Step { Index = 0, Node = new NodeAction { Type = "http_request", Inputs = JsonParser.Default.Parse<Struct>(inputs) } }, Cancel);

        completed.Outcome.ShouldBe(StepOutcome.Failed);
        completed.Error.ShouldContain(message);
        web.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancelled_or_timed_out_request_ends_as_such()
    {
        web.Delay = TimeSpan.FromSeconds(30);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var cancelled = await RunAsync(Node("http_request", new { url = "https://example.org" }), cancel.Token);
        var step = Node("http_request", new { url = "https://example.org" });
        step.Timeout = Duration.FromTimeSpan(TimeSpan.FromMilliseconds(200));
        var timedOut = await RunAsync(step, Cancel);

        cancelled.Outcome.ShouldBe(StepOutcome.Cancelled);
        timedOut.Outcome.ShouldBe(StepOutcome.TimedOut);
    }

    [Fact]
    public async Task An_unknown_node_type_is_an_error_of_the_runner() =>
        (await RunAsync(Node("publisher/custom", new { }), Cancel)).Outcome.ShouldBe(StepOutcome.Errored);

    [Fact]
    public async Task A_build_runs_buildkit_with_the_inputs_and_returns_the_digest()
    {
        var completed = await RunAsync(Node("docker_build", new
        {
            image = "ghcr.io/team/app",
            tags = Tags,
            context = "src",
            dockerfile = "build/Dockerfile",
            build_args = new { VERSION = "1.0" },
            target = "runtime",
            push = true,
            username = "bot",
            password = "s3cret-token",
        }, "s3cret-token"), Cancel);

        completed.Outcome.ShouldBe(StepOutcome.Succeeded, completed.Error);
        completed.Outputs["digest"].ShouldBe("sha256:abc");
        completed.Outputs["image"].ShouldBe("ghcr.io/team/app:1.0");
        var build = runtime.Helpers.ShouldHaveSingleItem();
        string.Join(' ', build.Command).ShouldBe(
            "buildctl-daemonless.sh build --progress plain --frontend dockerfile.v0 --local context=/workspace/src --local dockerfile=/workspace/src/build --opt filename=Dockerfile "
            + "--opt build-arg:VERSION=1.0 --opt target=runtime --output type=image,\"name=ghcr.io/team/app:1.0,ghcr.io/team/app:latest\",push=true --metadata-file /autocanvas/build-0/metadata.json");
        build.Env["DOCKER_CONFIG"].ShouldBe("/autocanvas/build-0");
        var auth = JsonDocument.Parse(runtime.DockerConfigs.ShouldHaveSingleItem()).RootElement.GetProperty("auths").GetProperty("ghcr.io").GetProperty("auth").GetString()!;
        Encoding.UTF8.GetString(Convert.FromBase64String(auth)).ShouldBe("bot:s3cret-token");
        var helper = runtime.Started.Values.Single(c => c.Image.StartsWith("moby/buildkit:", StringComparison.Ordinal));
        helper.SecurityOptions.ShouldBe(["seccomp=unconfined", "apparmor=unconfined"]);
        helper.Binds.ShouldContain(b => b == "autocanvas-buildkit-cache:/home/user/.local/share/buildkit");
        Directory.Exists(Path.Combine(options.WorkRoot, "job", "meta", "build-0")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_failed_build_fails_the_step_and_a_context_outside_the_workspace_is_refused()
    {
        runtime.Failing.Add("buildctl-daemonless.sh");

        var failed = await RunAsync(Node("docker_build", new { image = "app" }), Cancel);
        var outside = await RunAsync(Node("docker_build", new { image = "app", context = "../.." }), Cancel);

        failed.Outcome.ShouldBe(StepOutcome.Failed);
        failed.Error.ShouldContain("BuildKit exited with 128");
        outside.Outcome.ShouldBe(StepOutcome.Failed);
        outside.Error.ShouldContain("'context' must be a directory inside the workspace");
    }

    [Theory]
    [InlineData("app", "", "app:latest", "https://index.docker.io/v1/")]
    [InlineData("app:1.2", "", "app:1.2", "https://index.docker.io/v1/")]
    [InlineData("team/app", "a,b", "team/app:a|team/app:b", "https://index.docker.io/v1/")]
    [InlineData("registry.example.org:5000/app:old", "new", "registry.example.org:5000/app:new", "registry.example.org:5000")]
    [InlineData("localhost/app", "", "localhost/app:latest", "localhost")]
    public void Images_get_their_references_and_registry(string image, string tags, string references, string registry)
    {
        DockerBuildNode.References(image, tags.Length == 0 ? [] : tags.Split(',')).ShouldBe(references.Split('|'));
        DockerBuildNode.Registry(image).ShouldBe(registry);
    }

    public async ValueTask DisposeAsync()
    {
        server.Dispose();
        web.Dispose();
        if (Directory.Exists(options.DataDirectory))
        {
            Directory.Delete(options.DataDirectory, recursive: true);
        }

        await Task.CompletedTask;
    }

    private static Step Node(string type, object inputs, params string[] secrets)
    {
        var step = new Step { Index = 0, Node = new NodeAction { Type = type, Inputs = JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(inputs)) } };
        step.MaskValues.Add(secrets);
        return step;
    }

    private List<string> Logged()
    {
        var lines = new List<string>();
        while (outbox.Reader.TryRead(out var message))
        {
            if (message.MessageCase == ConnectRequest.MessageOneofCase.LogChunk)
            {
                lines.AddRange(message.LogChunk.Lines.Select(l => l.Text));
            }
        }

        return lines;
    }

    private async Task<StepCompleted> RunAsync(Step step, CancellationToken cancel)
    {
        using var http = new HttpClient(web, disposeHandler: false);
        var services = new JobServices(runtime, outbox, server, options, TimeProvider.System) { Http = http };
        var paths = new JobPaths(options, "job");
        Directory.CreateDirectory(paths.Workspace);
        Directory.CreateDirectory(paths.Meta);
        var log = new LogBatcher("job", outbox, TimeProvider.System);
        await using (log)
        {
            return await new NodeSteps(services, paths, Guid.CreateVersion7(), "job", log).RunAsync(step, cancel, Cancel);
        }
    }
}
