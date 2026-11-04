using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Variables;
using AutoCanvas.Server.Configuration;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoCanvas.Server.Tests;

public abstract class RepositoryWebhookTests(DatabaseProvider provider) : DatabaseTest(provider), IAsyncLifetime
{
    private const string Build = """
        version: 1
        triggers:
          push:
            branches: [main]
        jobs:
          build:
            steps:
              - run: make
        """;

    private const string Hooked = """
        version: 1
        triggers:
          webhook: true
        jobs:
          a:
            steps:
              - run: echo hi
        """;

    private const string Docs = """
        version: 1
        triggers:
          push:
            paths: ['docs/**']
        jobs:
          docs:
            steps:
              - run: make docs
        """;

    private static readonly string[] Added = ["docs/b.md"];

    private readonly string cache = Directory.CreateTempSubdirectory("autocanvas-repository-cache-").FullName;
    private GitHttpServer git = null!;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        git = await GitHttpServer.StartAsync();
        await git.CreateAsync("shop");
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await git.DisposeAsync();
        foreach (var file in Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(cache, recursive: true);
        await DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Editors_create_an_address_whose_token_and_secret_are_shown_once_and_delete_it()
    {
        await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build)), "Start");
        await using var factory = Factory();
        var (root, repository) = await ConnectAsync(factory);

        var created = await root.PostAsync(new Uri($"/api/v1/repositories/{repository}/webhook", UriKind.Relative), null, Cancel);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var hook = await created.JsonAsync();
        var token = hook.GetProperty("token").GetString()!;
        token.ShouldStartWith("acg_");
        hook.GetProperty("path").GetString().ShouldBe($"/api/v1/hooks/{token}");
        hook.GetProperty("secret").GetString()!.Length.ShouldBeGreaterThan(30);
        var view = await (await root.GetAsync(new Uri($"/api/v1/repositories/{repository}", UriKind.Relative), Cancel)).JsonAsync();
        view.GetProperty("webhook").GetProperty("hint").GetString().ShouldBe(hook.GetProperty("hint").GetString());
        view.GetRawText().ShouldNotContain(token);

        // A new address replaces the old one; deleting it ends both.
        (await root.PostAsync(new Uri($"/api/v1/repositories/{repository}/webhook", UriKind.Relative), null, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CallAsync(factory, token, "{}", ("X-GitHub-Event", "ping"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await root.DeleteAsync(new Uri($"/api/v1/repositories/{repository}/webhook", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await root.DeleteAsync(new Uri($"/api/v1/repositories/{repository}/webhook", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await root.GetAsync(new Uri($"/api/v1/repositories/{repository}", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("webhook").ValueKind.ShouldBe(JsonValueKind.Null);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().AuditEvents.CountAsync(e => e.Action == "repository.webhook_created", Cancel)).ShouldBe(2);
    }

    [Fact]
    public async Task A_signed_GitHub_push_starts_the_runs_with_its_pusher_and_changed_files()
    {
        var start = await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build), (".autocanvas/automations/docs.yml", Docs)), "Start");
        await using var factory = Factory();
        var (root, repository) = await ConnectAsync(factory);
        var (token, secret) = await WebhookAsync(root, repository);
        var pushed = await git.CommitAsync("shop", Files(("src/main.c", "int main;")), "Code");
        var body = GitHubPush("refs/heads/main", start, pushed, "Fix the build", ["src/main.c"]);

        var answer = await CallAsync(factory, token, body, ("X-GitHub-Event", "push"), ("X-Hub-Signature-256", Sign(secret, body)));

        answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await answer.JsonAsync()).GetProperty("queued").GetBoolean().ShouldBeTrue();
        (await HandleAsync(factory)).Started.Count.ShouldBe(1);
        var run = (await RunsAsync(factory)).ShouldHaveSingleItem();
        (run.AutomationName, run.Trigger, run.TriggeredByName, run.CommitSha).ShouldBe(("build", RunTriggerKind.Push, "octocat", pushed));
        var data = JsonDocument.Parse(run.TriggerData!).RootElement;
        data.GetProperty("message").GetString().ShouldBe("Fix the build");
        data.GetProperty("before").GetString().ShouldBe(start);

        // The poll that sees the same push starts nothing more.
        await PollAsync(factory);
        (await RunsAsync(factory)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Gitea_and_GitLab_pushes_are_understood_with_their_own_signatures()
    {
        var start = await git.CommitAsync("shop", Files((".autocanvas/automations/docs.yml", Docs)), "Start");
        await using var factory = Factory();
        var (root, repository) = await ConnectAsync(factory);
        var (token, secret) = await WebhookAsync(root, repository);

        var first = await git.CommitAsync("shop", Files(("docs/a.md", "a")), "Docs");
        var gitea = GitHubPush("refs/heads/main", start, first, "Docs", ["docs/a.md"], pusher: """{"login":"gitea-user","username":"gitea-user"}""");
        var signature = Sign(secret, gitea)["sha256=".Length..];
        (await CallAsync(factory, token, gitea, ("X-Gitea-Event", "push"), ("X-Gitea-Signature", signature))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await HandleAsync(factory);

        var second = await git.CommitAsync("shop", Files(("docs/b.md", "b")), "More docs");
        var gitlab = JsonSerializer.Serialize(new
        {
            object_kind = "push",
            @ref = "refs/heads/main",
            before = first,
            after = second,
            user_username = "gitlab-user",
            total_commits_count = 1,
            commits = new[] { new { id = second, message = "More docs\n", added = Added, modified = Array.Empty<string>(), removed = Array.Empty<string>() } },
        });
        (await CallAsync(factory, token, gitlab, ("X-Gitlab-Event", "Push Hook"), ("X-Gitlab-Token", secret))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await HandleAsync(factory);

        var runs = await RunsAsync(factory);
        runs.Select(r => (r.TriggeredByName, r.CommitSha)).ShouldBe([("gitea-user", first), ("gitlab-user", second)]);
        JsonDocument.Parse(runs[1].TriggerData!).RootElement.GetProperty("message").GetString().ShouldBe("More docs\n");
    }

    [Fact]
    public async Task Calls_without_the_secret_are_refused_and_other_events_do_nothing()
    {
        var start = await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build)), "Start");
        await using var factory = Factory();
        var (root, repository) = await ConnectAsync(factory);
        var (token, secret) = await WebhookAsync(root, repository);
        var body = GitHubPush("refs/heads/main", start, new string('a', 40), "x", []);

        (await CallAsync(factory, token, body, ("X-GitHub-Event", "push"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(factory, token, body, ("X-GitHub-Event", "push"), ("X-Hub-Signature-256", Sign("wrong", body)))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(factory, token, body, ("X-Gitlab-Token", "wrong"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallAsync(factory, "acg_unknown", body, ("X-Gitlab-Token", secret))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var ping = await CallAsync(factory, token, "{}", ("X-GitHub-Event", "ping"), ("X-Hub-Signature-256", Sign(secret, "{}")));
        (await ping.JsonAsync()).GetProperty("message").GetString().ShouldBe("pong");
        var issue = await CallAsync(factory, token, "{}", ("X-GitHub-Event", "issues"), ("X-Hub-Signature-256", Sign(secret, "{}")));
        issue.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await issue.JsonAsync()).GetProperty("queued").GetBoolean().ShouldBeFalse();
        var garbage = await CallAsync(factory, token, "not json", ("X-Hub-Signature-256", Sign(secret, "not json")));
        (await garbage.JsonAsync()).GetProperty("queued").GetBoolean().ShouldBeFalse();
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().RepositoryPushes.CountAsync(Cancel)).ShouldBe(0);
    }

    [Fact]
    public async Task A_late_webhook_for_a_push_a_poll_has_passed_starts_nothing_and_leaves_no_duplicate()
    {
        var start = await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build)), "Start");
        await using var factory = Factory();
        var (root, repository) = await ConnectAsync(factory);
        var (token, secret) = await WebhookAsync(root, repository);
        var second = await git.CommitAsync("shop", Files(("a.txt", "a")), "Second");
        var third = await git.CommitAsync("shop", Files(("b.txt", "b")), "Third");
        await PollAsync(factory);
        (await RunsAsync(factory)).ShouldHaveSingleItem().CommitSha.ShouldBe(third);

        var late = GitHubPush("refs/heads/main", start, second, "Second", ["a.txt"]);
        (await CallAsync(factory, token, late, ("X-GitHub-Event", "push"), ("X-Hub-Signature-256", Sign(secret, late)))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await HandleAsync(factory);
        await PollAsync(factory);

        (await RunsAsync(factory)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_server_handles_a_stored_push_right_away()
    {
        var start = await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build)), "Start");
        await using var factory = Factory(background: true);
        var (root, repository) = await ConnectAsync(factory);
        var (token, secret) = await WebhookAsync(root, repository);
        var pushed = await git.CommitAsync("shop", Files(("a.txt", "a")), "Second");
        var body = GitHubPush("refs/heads/main", start, pushed, "Second", ["a.txt"]);

        (await CallAsync(factory, token, body, ("X-GitHub-Event", "push"), ("X-Hub-Signature-256", Sign(secret, body)))).StatusCode.ShouldBe(HttpStatusCode.OK);

        for (var i = 0; i < 100 && (await RunsAsync(factory)).Count == 0; i++)
        {
            await Task.Delay(100, Cancel);
        }

        (await RunsAsync(factory)).ShouldHaveSingleItem().CommitSha.ShouldBe(pushed);
    }

    [Fact]
    public async Task A_rotated_master_key_carries_the_webhook_secrets_of_repositories_and_automations_along()
    {
        await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build)), "Start");
        await using var factory = Factory();
        var (root, repository) = await ConnectAsync(factory);
        var (_, secret) = await WebhookAsync(root, repository);
        var folder = (await (await root.GetAsync(new Uri($"/api/v1/repositories/{repository}", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("folderId").GetGuid();
        var managed = (await (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/automations", new { name = "hooked", yaml = Hooked }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        var signing = (await (await root.PostAsJsonAsync($"/api/v1/automations/{managed}/webhook", new { requireSignature = true }, Cancel)).JsonAsync()).GetProperty("signingSecret").GetString();

        await using var scope = factory.Services.CreateAsyncScope();
        var newKey = MasterKey.Generate();
        (await scope.ServiceProvider.GetRequiredService<MasterKeyGuard>().RotateAsync(newKey, Cancel)).Error.ShouldBeNull();

        var db = scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>();
        var cipher = new SecretCipher(newKey);
        var repositoryHook = await db.RepositoryWebhooks.AsNoTracking().SingleAsync(Cancel);
        cipher.Decrypt(RepositoryWebhookService.SecretScope, repository.ToString(), repositoryHook.SecretCiphertext).ShouldBe(secret);
        var automationHook = await db.AutomationWebhooks.AsNoTracking().SingleAsync(Cancel);
        cipher.Decrypt("webhook", managed.ToString(), automationHook.SigningSecret!).ShouldBe(signing);
    }

    /// <summary>The server; without its background handling of pushes unless <paramref name="background"/>, so that tests decide when.</summary>
    private AutoCanvasFactory Factory(bool background = false)
    {
        var factory = new AutoCanvasFactory().WithDatabase(Database).With("Repositories:CacheDirectory", cache);
        return background ? factory : factory.WithServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(RepositoryPollService)).ToList())
            {
                services.Remove(descriptor);
            }
        });
    }

    private static Dictionary<string, string?> Files(params (string Path, string? Content)[] files) =>
        files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

    private async Task<(HttpClient Root, Guid Repository)> ConnectAsync(AutoCanvasFactory factory)
    {
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var folder = (await (await root.PostAsJsonAsync("/api/v1/folders", new { name = "Shop" }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        var connected = await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = git.Url("shop").ToString(), pollMinutes = 0 }, Cancel);
        connected.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (root, (await connected.JsonAsync()).GetProperty("id").GetGuid());
    }

    private static async Task<(string Token, string Secret)> WebhookAsync(HttpClient client, Guid repository)
    {
        var hook = await (await client.PostAsync(new Uri($"/api/v1/repositories/{repository}/webhook", UriKind.Relative), null, Cancel)).JsonAsync();
        return (hook.GetProperty("token").GetString()!, hook.GetProperty("secret").GetString()!);
    }

    private static string GitHubPush(string gitRef, string before, string after, string message, string[] modified, string pusher = """{"name":"octocat","email":"octocat@example.org"}""") => $$"""
        {
          "ref": "{{gitRef}}",
          "before": "{{before}}",
          "after": "{{after}}",
          "forced": false,
          "pusher": {{pusher}},
          "head_commit": { "id": "{{after}}", "message": {{JsonSerializer.Serialize(message)}} },
          "commits": [ { "id": "{{after}}", "message": {{JsonSerializer.Serialize(message)}}, "added": [], "removed": [], "modified": {{JsonSerializer.Serialize(modified)}} } ]
        }
        """;

    private static string Sign(string secret, string body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    private static async Task<HttpResponseMessage> CallAsync(AutoCanvasFactory factory, string token, string body, params (string Name, string Value)[] headers)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/hooks/{token}") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request, Cancel);
    }

    private static async Task<Application.Triggers.TriggerRound> HandleAsync(AutoCanvasFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RepositoryPolling>().HandleReceivedAsync(Cancel);
    }

    /// <summary>Polls the repositories once, as if they were due (polling is off for these repositories otherwise).</summary>
    private static async Task PollAsync(AutoCanvasFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<AutoCanvasDbContext>().Repositories.ExecuteUpdateAsync(set => set.SetProperty(r => r.PollMinutes, 5).SetProperty(r => r.NextPollAt, (DateTimeOffset?)null), Cancel);
        await services.GetRequiredService<RepositoryPolling>().PollDueAsync(Cancel);
    }

    private static async Task<List<Run>> RunsAsync(AutoCanvasFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Runs.AsNoTracking().OrderBy(r => r.CreatedAt).ThenBy(r => r.AutomationName).ToListAsync(Cancel);
    }
}

public sealed class RepositoryWebhookTestsOnSqlite() : RepositoryWebhookTests(DatabaseProvider.Sqlite);

public sealed class RepositoryWebhookTestsOnPostgres() : RepositoryWebhookTests(DatabaseProvider.Postgres);
