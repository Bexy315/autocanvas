using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Runner.Connection;
using AutoCanvas.Runner.Docker;
using AutoCanvas.Runner.Execution;
using AutoCanvas.Server.Tests;
using AutoCanvas.TestSupport;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Runner.Tests;

/// <summary>
/// The server in memory and a runner connected to it, as tests drive them: the runner's gRPC
/// channel goes through the test server, and its containers are <see cref="FakeRuntime"/>'s (or
/// real ones, for the Docker tests).
/// </summary>
internal sealed class TestRunner : IAsyncDisposable
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly List<IHost> hosts = [];
    private readonly bool ownsServer;
    private BreakableHandler? handler;

    private TestRunner(AutoCanvasFactory server, HttpClient admin, string dataDirectory, IContainerRuntime runtime, bool ownsServer = true)
    {
        Server = server;
        Admin = admin;
        DataDirectory = dataDirectory;
        Runtime = runtime;
        this.ownsServer = ownsServer;
    }

    public AutoCanvasFactory Server { get; }

    public HttpClient Admin { get; }

    public string DataDirectory { get; }

    public IContainerRuntime Runtime { get; }

    /// <summary>The handler of the runner's last channel, to break its control stream.</summary>
    public BreakableHandler Network => handler ?? throw new InvalidOperationException("No runner was started.");

    public static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>A server and a runner for one test; the server on <paramref name="database"/> if given, with extra settings.</summary>
    public static async Task<TestRunner> CreateAsync(IContainerRuntime? runtime = null, TestDatabase? database = null, params (string Key, string Value)[] settings)
    {
        var server = new AutoCanvasFactory().With("Runners:HeartbeatInterval", "00:00:01");
        if (database is not null)
        {
            server.WithDatabase(database);
        }

        foreach (var (key, value) in settings)
        {
            server.With(key, value);
        }

        var admin = await server.SignedInAsync("root", isAdmin: true);
        var data = Path.Combine(Path.GetTempPath(), "autocanvas-runner-tests", Guid.NewGuid().ToString("N"));
        return new TestRunner(server, admin, data, runtime ?? new FakeRuntime());
    }

    /// <summary>A second runner on the same server, with a data directory of its own.</summary>
    public TestRunner Another() => new(Server, Admin, DataDirectory + "-another", Runtime, ownsServer: false);

    /// <summary>The arguments that point the runner at the test server and its own data directory.</summary>
    public string[] Settings(params string[] more) =>
        [$"--Runner:Server={Server.Server.BaseAddress}", $"--Runner:DataDirectory={DataDirectory}", "--Runner:ShutdownTimeout=00:00:05", .. more];

    /// <summary>Replaces the runner's channel and container engine with the test's.</summary>
    public void Configure(IServiceCollection services)
    {
        services.RemoveAll<ServerChannel>();
        services.AddSingleton(_ =>
        {
            handler = new BreakableHandler(Server.Server.CreateHandler());
            return new ServerChannel(GrpcChannel.ForAddress(Server.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = handler }));
        });
        services.RemoveAll<IContainerRuntime>();
        services.AddSingleton(Runtime);
        services.RemoveAll<JobServices>();
        services.AddSingleton(provider => new JobServices(
            Runtime,
            provider.GetRequiredService<Outbox>(),
            provider.GetRequiredService<ServerChannel>(),
            provider.GetRequiredService<IOptions<RunnerOptions>>().Value,
            TimeProvider.System)
        {
            GracePeriod = TimeSpan.FromMilliseconds(300),
        });
    }

    public async Task<string> TokenAsync()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/runner-registration-tokens", new { description = "test" }, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("token").GetString()!;
    }

    /// <summary>Registers through the command line, as an operator would.</summary>
    public async Task<int> RegisterAsync(string name = "runner-1", string? token = null, StringWriter? output = null, StringWriter? error = null) =>
        await RunnerCommandLine.RunAsync(["register", "--token", token ?? await TokenAsync(), "--name", name, .. Settings()], output ?? new StringWriter(), error ?? new StringWriter(), Configure);

    /// <summary>Starts a registered runner; it is stopped when the test ends.</summary>
    public async Task<IHost> StartAsync(params string[] settings)
    {
        var builder = RunnerHost.CreateBuilder(Settings(settings));
        Configure(builder.Services);
        var host = builder.Build();
        hosts.Add(host);
        await host.StartAsync(Cancel);
        return host;
    }

    public async Task<Guid> StartRunAsync(string yaml)
    {
        var folder = Id(await (await Admin.PostAsJsonAsync("/api/v1/folders", new { name = "Team" + Guid.NewGuid().ToString("N")[..8] }, Cancel)).JsonAsync());
        return await StartRunAsync(yaml, folder);
    }

    public async Task<Guid> StartRunAsync(string yaml, Guid folder)
    {
        var automation = Id(await (await Admin.PostAsJsonAsync($"/api/v1/folders/{folder}/automations", new { name = "build", yaml }, Cancel)).JsonAsync());
        var started = await Admin.PostAsync(new Uri($"/api/v1/automations/{automation}/runs", UriKind.Relative), null, Cancel);
        started.StatusCode.ShouldBe(HttpStatusCode.Created);
        return Id(await started.JsonAsync());
    }

    public async Task<JsonElement> GetAsync(string path) => await (await Admin.GetAsync(new Uri(path, UriKind.Relative), Cancel)).JsonAsync();

    /// <summary>The run once it has ended, within <paramref name="patience"/> (<see cref="Patience"/> by default).</summary>
    public async Task<JsonElement> EndedRunAsync(Guid run, TimeSpan? patience = null)
    {
        var until = DateTimeOffset.UtcNow + (patience ?? Patience);
        while (true)
        {
            var view = await GetAsync($"/api/v1/runs/{run}");
            if (view.GetProperty("state").GetString() is not "Running")
            {
                return view;
            }

            if (DateTimeOffset.UtcNow >= until)
            {
                throw new ShouldAssertException($"The run did not end in time. Its log:{Environment.NewLine}{string.Join(Environment.NewLine, await LogAsync(run))}");
            }

            await Task.Delay(100, Cancel);
        }
    }

    /// <summary>Waits until <paramref name="condition"/> holds, within <see cref="Patience"/>.</summary>
    public static async Task EventuallyAsync(Func<Task<bool>> condition, string what)
    {
        var until = DateTimeOffset.UtcNow + Patience;
        while (!await condition())
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(until, what);
            await Task.Delay(50, Cancel);
        }
    }

    /// <summary>All lines of the job's first attempt, in order.</summary>
    public async Task<List<string>> LogAsync(Guid run, string job = "build")
    {
        var detail = await GetAsync($"/api/v1/runs/{run}/jobs/{job}");
        var attempt = detail.GetProperty("attempts")[0].GetProperty("id").GetString();
        var page = await GetAsync($"/api/v1/runs/{run}/attempts/{attempt}/log");
        return [.. page.GetProperty("chunks").EnumerateArray().SelectMany(c => c.GetProperty("lines").EnumerateArray()).Select(l => l.GetProperty("text").GetString()!)];
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in hosts)
        {
            using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await host.StopAsync(stopping.Token);
            host.Dispose();
        }

        if (ownsServer)
        {
            Admin.Dispose();
            await Server.DisposeAsync();
        }

        if (Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();
}
