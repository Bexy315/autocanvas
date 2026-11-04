using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Reports;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Forges;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Configuration;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoCanvas.Server.Tests;

public abstract class ForgeReportingTests(DatabaseProvider provider) : DatabaseTest(provider), IAsyncLifetime
{
    private const string Build = """
        version: 1
        triggers:
          manual: true
          pull_request: {}
        jobs:
          build:
            steps:
              - run: make
        """;

    private const long ShopId = 9001;

    private readonly string cache = Directory.CreateTempSubdirectory("autocanvas-repository-cache-").FullName;
    private GitHubStub github = null!;
    private string main = null!;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        github = await GitHubStub.StartAsync();
        await github.Git.CreateAsync("acme/shop");
        github.Install(7, "acme", (ShopId, "shop"));
        main = await github.Git.CommitAsync("acme/shop", new Dictionary<string, string?>(StringComparer.Ordinal) { [".autocanvas/automations/build.yml"] = Build }, "Start");
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
    public async Task A_run_of_a_GitHub_repository_sets_a_pending_and_then_a_final_commit_status_once_each()
    {
        await using var factory = Factory();
        var (root, folder) = await ConnectAsync(factory);
        var automation = (await (await root.GetAsync(new Uri($"/api/v1/folders/{folder}/automations", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        var run = (await (await root.PostAsync(new Uri($"/api/v1/automations/{automation}/runs", UriKind.Relative), null, Cancel)).JsonAsync()).GetProperty("id").GetGuid();

        await ReportAsync(factory);
        var pending = github.Statuses.ShouldHaveSingleItem();
        pending.Commit.ShouldBe(main);
        pending.Status.GetProperty("state").GetString().ShouldBe("pending");
        pending.Status.GetProperty("context").GetString().ShouldBe("AutoCanvas / build (manual)");
        pending.Status.GetProperty("target_url").GetString().ShouldBe($"http://autocanvas.test/runs/{run}");

        await FinishAsync(factory, run, AttemptOutcome.Succeeded);
        await ReportAsync(factory);
        await ReportAsync(factory);

        github.Statuses.Count.ShouldBe(2);
        var done = github.Statuses.Last().Status;
        done.GetProperty("state").GetString().ShouldBe("success");
        done.GetProperty("description").GetString()!.ShouldStartWith("Succeeded");
        github.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pull_request_gets_one_comment_with_the_results_which_later_runs_update()
    {
        await using var factory = Factory();
        var (root, _) = await ConnectAsync(factory);
        var first = await HeadAsync("First try");
        await PullRequestAsync(factory, "d1", "opened", first);
        var run = (await RunsAsync(factory)).ShouldHaveSingleItem();
        await ReportsAsync(factory, run.Id, passed: 8, failed: 2, linesCovered: 84, linesValid: 100);
        await FinishAsync(factory, run.Id, AttemptOutcome.Failed);

        await ReportAsync(factory);

        var failure = github.Statuses.Last();
        (failure.Commit, failure.Status.GetProperty("state").GetString()).ShouldBe((first, "failure"));
        failure.Status.GetProperty("context").GetString().ShouldBe("AutoCanvas / build (pull_request)");
        failure.Status.GetProperty("description").GetString()!.ShouldEndWith("2 of 10 tests failed");
        var (id, comment) = github.Comments.ShouldHaveSingleItem();
        comment.Number.ShouldBe(12);
        comment.Body.ShouldStartWith("<!-- autocanvas -->");
        comment.Body.ShouldContain($"Runs for {first[..7]}");
        comment.Body.ShouldContain($"[build #1](http://autocanvas.test/runs/{run.Id})");
        comment.Body.ShouldContain("❌ Failed | 8 passed, 2 failed | 84.0 % lines");

        // New commits: the same comment shows the new head.
        var second = await HeadAsync("Second try");
        await PullRequestAsync(factory, "d2", "synchronize", second);
        var again = (await RunsAsync(factory)).Single(r => string.Equals(r.CommitSha, second, StringComparison.Ordinal));
        await FinishAsync(factory, again.Id, AttemptOutcome.Succeeded);
        await ReportAsync(factory);
        var updated = github.Comments.ShouldHaveSingleItem();
        updated.Key.ShouldBe(id);
        updated.Value.Body.ShouldContain($"Runs for {second[..7]}");
        updated.Value.Body.ShouldContain("build #2");
        updated.Value.Body.ShouldContain("✅ Succeeded | – | –");

        // Someone deleted it: the next result makes a new one. A cancelled run is an error.
        github.DeleteComment(id);
        var third = await HeadAsync("Third try");
        await PullRequestAsync(factory, "d3", "synchronize", third);
        var cancelled = (await RunsAsync(factory)).Single(r => string.Equals(r.CommitSha, third, StringComparison.Ordinal));
        (await root.PostAsync(new Uri($"/api/v1/runs/{cancelled.Id}/cancel", UriKind.Relative), null, Cancel)).EnsureSuccessStatusCode();
        await FinishAsync(factory, cancelled.Id, AttemptOutcome.Cancelled);
        await ReportAsync(factory);
        github.Statuses.Last().Status.GetProperty("state").GetString().ShouldBe("error");
        var renewed = github.Comments.ShouldHaveSingleItem();
        renewed.Key.ShouldNotBe(id);
        renewed.Value.Body.ShouldContain("⏹️ Cancelled");
    }

    [Fact]
    public async Task A_failed_report_is_logged_and_not_tried_again()
    {
        await using var factory = Factory();
        var (root, folder) = await ConnectAsync(factory);
        var automation = (await (await root.GetAsync(new Uri($"/api/v1/folders/{folder}/automations", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // The installation is gone: GitHub hands out no token.
            await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Repositories.ExecuteUpdateAsync(set => set.SetProperty(r => r.InstallationId, 99L), Cancel);
        }

        (await root.PostAsync(new Uri($"/api/v1/automations/{automation}/runs", UriKind.Relative), null, Cancel)).EnsureSuccessStatusCode();

        (await ReportAsync(factory)).Skipped.ShouldHaveSingleItem().ShouldContain("was not reported to GitHub");
        (await ReportAsync(factory)).Skipped.ShouldBeEmpty();
        github.Statuses.ShouldBeEmpty();
    }

    /// <summary>The server with the app, without its background handling, so that tests decide when reports go out.</summary>
    private AutoCanvasFactory Factory() =>
        new AutoCanvasFactory().WithDatabase(Database).With("Repositories:CacheDirectory", cache).With("Server:PublicUrl", "http://autocanvas.test").WithServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(RepositoryPollService)).ToList())
            {
                services.Remove(descriptor);
            }
        });

    private async Task<(HttpClient Root, Guid Folder)> ConnectAsync(AutoCanvasFactory factory)
    {
        var root = await factory.SignedInAsync("root", isAdmin: true);
        (await root.PutAsJsonAsync("/api/v1/forges/github", new { appId = GitHubStub.AppId, privateKey = github.PrivateKey, webhookSecret = github.WebhookSecret, webAddress = github.Web.ToString() }, Cancel)).EnsureSuccessStatusCode();
        var folder = (await (await root.PostAsJsonAsync("/api/v1/folders", new { name = "Shop" }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories/github", new { installationId = 7, repository = "acme/shop", pollMinutes = 0 }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Created);
        return (root, folder);
    }

    /// <summary>A new head of pull request 12.</summary>
    private async Task<string> HeadAsync(string message)
    {
        var head = await github.Git.CommitAsync("acme/shop", new Dictionary<string, string?>(StringComparer.Ordinal) { ["README.md"] = message }, message, branch: "feature");
        await github.Git.SetRefAsync("acme/shop", "refs/pull/12/head", head);
        return head;
    }

    private async Task PullRequestAsync(AutoCanvasFactory factory, string delivery, string action, string head)
    {
        var body = JsonSerializer.Serialize(new
        {
            action,
            pull_request = new
            {
                number = 12,
                title = "Faster",
                user = new { login = "octocat" },
                head = new { @ref = "feature", sha = head, repo = new { id = ShopId } },
                @base = new { @ref = "main", repo = new { id = ShopId } },
            },
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/forges/github/events") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-GitHub-Event", "pull_request");
        request.Headers.Add("X-GitHub-Delivery", delivery);
        request.Headers.Add("X-Hub-Signature-256", github.Sign(body));
        (await client.SendAsync(request, Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RepositoryPolling>().HandleReceivedAsync(Cancel);
    }

    private static async Task<TriggerRoundView> ReportAsync(AutoCanvasFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var round = await scope.ServiceProvider.GetRequiredService<ForgeReporter>().ReportAsync(Cancel);
        return new TriggerRoundView(round.Started, round.Skipped);
    }

    /// <summary>Ends the run's only attempt as a runner would.</summary>
    private static async Task FinishAsync(AutoCanvasFactory factory, Guid run, AttemptOutcome outcome)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>();
        var attempt = await db.JobAttempts.Where(a => a.Job!.RunId == run).Select(a => a.Id).SingleAsync(Cancel);
        await db.JobAttempts.Where(a => a.Id == attempt && a.Status == AttemptStatus.Queued).ExecuteUpdateAsync(set => set.SetProperty(a => a.Status, AttemptStatus.Running), Cancel);
        await scope.ServiceProvider.GetRequiredService<JobDispatcher>().FinishAsync(attempt, null, outcome, null, Cancel);
    }

    /// <summary>A test report and a coverage report of the run's attempt, as a runner would upload them.</summary>
    private static async Task ReportsAsync(AutoCanvasFactory factory, Guid run, int passed, int failed, int linesCovered, int linesValid)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>();
        var attempt = await db.JobAttempts.Where(a => a.Job!.RunId == run).Select(a => a.Id).SingleAsync(Cancel);
        db.Reports.Add(new Report
        {
            Id = Guid.CreateVersion7(),
            AttemptId = attempt,
            RunId = run,
            JobKey = "build",
            Name = "Tests",
            Format = "junit",
            Kind = ReportKind.Tests,
            Path = "tests.xml",
            Size = 10,
            ReceivedAt = DateTimeOffset.UtcNow,
            TestsTotal = passed + failed,
            TestsPassed = passed,
            TestsFailed = failed,
            TestsSkipped = 0,
            TestsDurationMs = 1000,
        });
        db.Reports.Add(new Report
        {
            Id = Guid.CreateVersion7(),
            AttemptId = attempt,
            RunId = run,
            JobKey = "build",
            Name = "Coverage",
            Format = "cobertura",
            Kind = ReportKind.Coverage,
            Path = "coverage.xml",
            Size = 10,
            ReceivedAt = DateTimeOffset.UtcNow,
            LinesCovered = linesCovered,
            LinesValid = linesValid,
            BranchesCovered = 0,
            BranchesValid = 0,
        });
        await db.SaveChangesAsync(Cancel);
    }

    private static async Task<List<Run>> RunsAsync(AutoCanvasFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Runs.AsNoTracking().OrderBy(r => r.Number).ToListAsync(Cancel);
    }

    private sealed record TriggerRoundView(IReadOnlyList<string> Started, IReadOnlyList<string> Skipped);
}

public sealed class ForgeReportingTestsOnSqlite() : ForgeReportingTests(DatabaseProvider.Sqlite);

public sealed class ForgeReportingTestsOnPostgres() : ForgeReportingTests(DatabaseProvider.Postgres);
