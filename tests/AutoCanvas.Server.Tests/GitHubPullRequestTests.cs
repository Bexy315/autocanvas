using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using AutoCanvas.Server.Configuration;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoCanvas.Server.Tests;

public abstract class GitHubPullRequestTests(DatabaseProvider provider) : DatabaseTest(provider), IAsyncLifetime
{
    private const string Build = """
        version: 1
        triggers:
          pull_request:
            branches: [main]
        jobs:
          build:
            env:
              TOKEN: ${{ secrets.DEPLOY_TOKEN }}
            steps:
              - run: make
        """;

    private const string Docs = """
        version: 1
        triggers:
          pull_request:
            paths: ['docs/**']
        jobs:
          docs:
            steps:
              - run: make docs
        """;

    private const long ShopId = 9001;
    private const int Number = 12;

    private readonly string cache = Directory.CreateTempSubdirectory("autocanvas-repository-cache-").FullName;
    private GitHubStub github = null!;
    private string main = null!;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        github = await GitHubStub.StartAsync();
        await github.Git.CreateAsync("acme/shop");
        github.Install(7, "acme", (ShopId, "shop"));
        main = await github.Git.CommitAsync("acme/shop", Files((".autocanvas/automations/build.yml", Build), (".autocanvas/automations/docs.yml", Docs)), "Start");
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await github.DisposeAsync();
        foreach (var file in Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(cache, recursive: true);
        await DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task An_opened_pull_request_runs_the_file_at_its_head_with_its_details()
    {
        await using var factory = Factory();
        var root = await ConnectAsync(factory);
        var head = await github.Git.CommitAsync("acme/shop", Files((".autocanvas/automations/build.yml", Build + "# from the branch\n")), "Try it", branch: "feature/x");
        await github.Git.SetRefAsync("acme/shop", "refs/pull/12/head", head);
        github.SetPullRequestFiles(Number, "src/main.c");

        var answer = await SendAsync(factory, "d1", PullRequest("opened", head));

        (await answer.JsonAsync()).GetProperty("queued").GetBoolean().ShouldBeTrue();
        await HandleAsync(factory);
        var run = (await RunsAsync(factory)).ShouldHaveSingleItem();
        (run.AutomationName, run.Trigger, run.TriggeredByName, run.CommitSha, run.Ref, run.PullRequest, run.SecretsWithheld)
            .ShouldBe(("build", RunTriggerKind.PullRequest, "octocat", head, "refs/pull/12/head", Number, false));
        run.Yaml.ShouldContain("# from the branch");
        var data = JsonDocument.Parse(run.TriggerData!).RootElement;
        data.GetProperty("number").GetString().ShouldBe("12");
        data.GetProperty("title").GetString().ShouldBe("Make it faster");
        data.GetProperty("head_branch").GetString().ShouldBe("feature/x");
        data.GetProperty("base_branch").GetString().ShouldBe("main");
        data.GetProperty("fork").GetString().ShouldBe("false");
        data.GetProperty("url").GetString().ShouldBe("https://github.com/acme/shop/pull/12");

        // The job checks out the head with a token that may only read this repository; its secret goes with its steps.
        var (checkout, env) = await OfferAsync(factory, run.Id);
        (checkout.Url, checkout.Ref, checkout.Password).ShouldBe((github.Git.Url("acme/shop"), head, github.Token));
        env.ShouldNotContainKey("TOKEN");

        // The same delivery again, a closed pull request, and one into another branch start nothing.
        await SendAsync(factory, "d1", PullRequest("opened", head));
        await SendAsync(factory, "d2", PullRequest("closed", head));
        await SendAsync(factory, "d3", PullRequest("opened", head, baseBranch: "develop"));
        await HandleAsync(factory);
        (await RunsAsync(factory)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Path_filters_read_the_changed_files_and_a_moved_head_starts_nothing()
    {
        await using var factory = Factory();
        await ConnectAsync(factory);
        var first = await github.Git.CommitAsync("acme/shop", Files(("docs/index.md", "docs")), "Docs", branch: "docs");
        await github.Git.SetRefAsync("acme/shop", "refs/pull/12/head", first);
        github.SetPullRequestFiles(Number, "docs/index.md");

        await SendAsync(factory, "d1", PullRequest("opened", first));
        await HandleAsync(factory);
        (await RunsAsync(factory)).Select(r => r.AutomationName).ShouldBe(["build", "docs"], ignoreOrder: true);

        // An event for a head the pull request has moved past.
        var second = await github.Git.CommitAsync("acme/shop", Files(("src/a.c", "a")), "Code", branch: "docs");
        await github.Git.SetRefAsync("acme/shop", "refs/pull/12/head", second);
        await SendAsync(factory, "d2", PullRequest("synchronize", first));
        await HandleAsync(factory);
        (await RunsAsync(factory)).Count.ShouldBe(2);

        // Code only: the docs automation does not run.
        github.SetPullRequestFiles(Number, "docs/index.md", "src/a.c");
        github.SetPullRequestFiles(Number, "src/a.c");
        await SendAsync(factory, "d3", PullRequest("synchronize", second));
        await HandleAsync(factory);
        (await RunsAsync(factory)).Where(r => string.Equals(r.CommitSha, second, StringComparison.Ordinal)).Select(r => r.AutomationName).ShouldBe(["build"]);
    }

    [Fact]
    public async Task A_pull_request_from_a_fork_runs_without_secrets_and_other_repositories()
    {
        await using var factory = Factory();
        await ConnectAsync(factory);
        var head = await github.Git.CommitAsync("acme/shop", Files((".autocanvas/automations/build.yml", Build + "\n"), ("README.md", "fork")), "From a fork", branch: "fork");
        await github.Git.SetRefAsync("acme/shop", "refs/pull/12/head", head);

        await SendAsync(factory, "d1", PullRequest("opened", head, fork: true));
        await HandleAsync(factory);

        var run = (await RunsAsync(factory)).Single(r => string.Equals(r.AutomationName, "build", StringComparison.Ordinal));
        run.SecretsWithheld.ShouldBeTrue();
        JsonDocument.Parse(run.TriggerData!).RootElement.GetProperty("fork").GetString().ShouldBe("true");
        // No secret: the value is plain and empty.
        var (checkout, env) = await OfferAsync(factory, run.Id);
        checkout.Ref.ShouldBe(head);
        env["TOKEN"].ShouldBeEmpty();
    }

    /// <summary>The server with the app set up and without its background handling of events.</summary>
    private AutoCanvasFactory Factory() =>
        new AutoCanvasFactory().WithDatabase(Database).With("Repositories:CacheDirectory", cache).With("Server:PublicUrl", "http://autocanvas.test").WithServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(RepositoryPollService)).ToList())
            {
                services.Remove(descriptor);
            }
        });

    private async Task<HttpClient> ConnectAsync(AutoCanvasFactory factory)
    {
        var root = await factory.SignedInAsync("root", isAdmin: true);
        (await root.PutAsJsonAsync("/api/v1/forges/github", new { appId = GitHubStub.AppId, privateKey = github.PrivateKey, webhookSecret = github.WebhookSecret, webAddress = github.Web.ToString() }, Cancel)).EnsureSuccessStatusCode();
        var folder = (await (await root.PostAsJsonAsync("/api/v1/folders", new { name = "Shop" }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var actor = new Application.Common.Actor(await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Users.Where(u => u.UserName == "root").Select(u => u.Id).SingleAsync(Cancel), IsAdmin: true);
        (await scope.ServiceProvider.GetRequiredService<SecretService>().SetAsync(actor, ConfigurationScope.Folder(folder), "DEPLOY_TOKEN", "t0ken", Cancel)).Error.ShouldBeNull();
        (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories/github", new { installationId = 7, repository = "acme/shop", pollMinutes = 0 }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Created);
        return root;
    }

    private static Dictionary<string, string?> Files(params (string Path, string? Content)[] files) =>
        files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

    private string PullRequest(string action, string head, string baseBranch = "main", bool fork = false) => JsonSerializer.Serialize(new
    {
        action,
        number = Number,
        pull_request = new
        {
            number = Number,
            title = "Make it faster",
            html_url = "https://github.com/acme/shop/pull/12",
            user = new { login = "octocat" },
            head = new { @ref = "feature/x", sha = head, repo = new { id = fork ? 4711 : ShopId } },
            @base = new { @ref = baseBranch, sha = main, repo = new { id = ShopId } },
        },
        repository = new { id = ShopId },
    });

    private async Task<HttpResponseMessage> SendAsync(AutoCanvasFactory factory, string delivery, string body)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/forges/github/events") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-GitHub-Event", "pull_request");
        request.Headers.Add("X-GitHub-Delivery", delivery);
        request.Headers.Add("X-Hub-Signature-256", github.Sign(body));
        var response = await client.SendAsync(request, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return response;
    }

    private static async Task HandleAsync(AutoCanvasFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RepositoryPolling>().HandleReceivedAsync(Cancel);
    }

    private static async Task<List<Run>> RunsAsync(AutoCanvasFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Runs.AsNoTracking().OrderBy(r => r.CreatedAt).ThenBy(r => r.AutomationName).ToListAsync(Cancel);
    }

    /// <summary>The job of a run as offered to a runner: its checkout and its environment without secrets.</summary>
    private static async Task<(OfferedCheckout Checkout, IReadOnlyDictionary<string, string> Env)> OfferAsync(AutoCanvasFactory factory, Guid run)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var attempt = await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().JobAttempts.AsNoTracking().Where(a => a.Job!.RunId == run).Select(a => a.Id).SingleAsync(Cancel);
        var offered = (await scope.ServiceProvider.GetRequiredService<JobOfferBuilder>().BuildAsync(attempt, Cancel)).Value!;
        return (offered.Checkouts.ShouldHaveSingleItem(), offered.Env);
    }
}

public sealed class GitHubPullRequestTestsOnSqlite() : GitHubPullRequestTests(DatabaseProvider.Sqlite);

public sealed class GitHubPullRequestTestsOnPostgres() : GitHubPullRequestTests(DatabaseProvider.Postgres);
