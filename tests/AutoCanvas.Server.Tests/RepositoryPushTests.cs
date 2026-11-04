using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Runs;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Server.Tests;

public abstract class RepositoryPushTests(DatabaseProvider provider) : DatabaseTest(provider), IAsyncLifetime
{
    private const string Build = """
        version: 1
        triggers:
          push:
            branches: [main, 'feature/*']
            tags: ['v*']
        jobs:
          build:
            steps:
              - run: make
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

    private const string Manual = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            steps:
              - run: make
        """;

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
    public async Task A_push_to_the_branch_syncs_the_automations_and_starts_the_runs_its_triggers_call_for()
    {
        await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build), (".autocanvas/automations/manual.yml", Manual)), "Start");
        await using var factory = Factory();
        var (root, repository, folder) = await ConnectAsync(factory);
        (await PollAsync(factory)).Started.ShouldBeEmpty();

        // What was there when it was connected starts nothing; a new commit does.
        var pushed = await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build + "# second\n")), "Second build", author: "Grace Hopper");
        await PollAsync(factory);

        var build = await AutomationIdAsync(root, folder, "build");
        var run = (await RunsAsync(factory, build)).ShouldHaveSingleItem();
        run.Trigger.ShouldBe(RunTriggerKind.Push);
        run.TriggeredByName.ShouldBe("Grace Hopper");
        (run.CommitSha, run.Ref, run.AutomationVersion).ShouldBe((pushed, "refs/heads/main", 2));
        run.Yaml.ShouldContain("# second");
        var data = JsonDocument.Parse(run.TriggerData!).RootElement;
        data.GetProperty("branch").GetString().ShouldBe("main");
        data.GetProperty("ref").GetString().ShouldBe("refs/heads/main");
        data.GetProperty("sha").GetString().ShouldBe(pushed);
        data.GetProperty("short_sha").GetString().ShouldBe(pushed[..7]);
        data.GetProperty("before").GetString()!.Length.ShouldBe(40);
        data.GetProperty("pusher").GetString().ShouldBe("Grace Hopper");
        data.GetProperty("message").GetString().ShouldBe("Second build");
        (await RunsAsync(factory, await AutomationIdAsync(root, folder, "manual"))).ShouldBeEmpty();

        // The same state again starts nothing.
        await PollAsync(factory);
        (await RunsAsync(factory, build)).Count.ShouldBe(1);
        (await (await root.GetAsync(new Uri($"/api/v1/repositories/{repository}", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("lastCommit").GetString().ShouldBe(pushed);
    }

    [Fact]
    public async Task A_push_to_another_branch_or_a_tag_runs_the_file_there_without_a_new_version()
    {
        var start = await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build), (".autocanvas/automations/only-main.yml", Manual)), "Start");
        await using var factory = Factory();
        var (root, _, folder) = await ConnectAsync(factory);
        var build = await AutomationIdAsync(root, folder, "build");

        var feature = await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build + "# feature\n"), (".autocanvas/automations/new.yml", Build)), "Try it", branch: "feature/fast");
        await git.CommitAsync("shop", Files(("README.md", "other")), "Other", branch: "other");
        await git.TagAsync("shop", "v1.0", start, message: "First release");
        await git.TagAsync("shop", "nightly", start);
        await PollAsync(factory);

        var runs = await RunsAsync(factory, build);
        runs.Select(r => r.Ref).ShouldBe(["refs/heads/feature/fast", "refs/tags/v1.0"], ignoreOrder: true);
        var onFeature = runs.Single(r => string.Equals(r.Ref, "refs/heads/feature/fast", StringComparison.Ordinal));
        (onFeature.CommitSha, onFeature.AutomationVersion).ShouldBe((feature, 1));
        onFeature.Yaml.ShouldContain("# feature");
        var onTag = runs.Single(r => string.Equals(r.Ref, "refs/tags/v1.0", StringComparison.Ordinal));
        onTag.CommitSha.ShouldBe(start);
        JsonDocument.Parse(onTag.TriggerData!).RootElement.GetProperty("tag").GetString().ShouldBe("v1.0");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>();
        (await db.AutomationVersions.CountAsync(v => v.AutomationId == build, Cancel)).ShouldBe(1);

        // A file only on the branch is no automation of the folder yet.
        (await db.Automations.CountAsync(a => a.FolderId == folder, Cancel)).ShouldBe(2);
    }

    [Fact]
    public async Task Path_filters_look_at_the_changed_files_and_deleted_branches_start_nothing()
    {
        await git.CommitAsync("shop", Files((".autocanvas/automations/docs.yml", Docs), ("docs/index.md", "one")), "Start");
        await using var factory = Factory();
        var (root, _, folder) = await ConnectAsync(factory);
        var docs = await AutomationIdAsync(root, folder, "docs");

        await git.CommitAsync("shop", Files(("src/main.c", "int main;")), "Code only");
        await PollAsync(factory);
        (await RunsAsync(factory, docs)).ShouldBeEmpty();

        var changed = await git.CommitAsync("shop", Files(("docs/index.md", "two")), "Docs");
        await PollAsync(factory);
        (await RunsAsync(factory, docs)).ShouldHaveSingleItem().CommitSha.ShouldBe(changed);

        // A new branch has no known changes, so any change counts; its deletion starts nothing.
        await git.CommitAsync("shop", Files(("src/other.c", "x")), "New branch", branch: "topic");
        await PollAsync(factory);
        (await RunsAsync(factory, docs)).Count.ShouldBe(2);
        await git.DeleteRefAsync("shop", "refs/heads/topic");
        await PollAsync(factory);
        (await RunsAsync(factory, docs)).Count.ShouldBe(2);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().RepositoryRefs.Select(r => r.Ref).ToListAsync(Cancel)).ShouldBe(["refs/heads/main"]);
    }

    [Fact]
    public async Task Polling_is_set_per_repository_and_a_failed_poll_is_shown_on_it()
    {
        await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build)), "Start");
        await using var factory = Factory();
        var (root, repository, _) = await ConnectAsync(factory);
        var view = await (await root.GetAsync(new Uri($"/api/v1/repositories/{repository}", UriKind.Relative), Cancel)).JsonAsync();
        view.GetProperty("pollMinutes").GetInt32().ShouldBe(5);
        view.GetProperty("nextPollAt").ValueKind.ShouldBe(JsonValueKind.String);

        (await root.PatchAsJsonAsync($"/api/v1/repositories/{repository}", new { pollMinutes = 1441 }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var off = await (await root.PatchAsJsonAsync($"/api/v1/repositories/{repository}", new { pollMinutes = 0 }, Cancel)).JsonAsync();
        off.GetProperty("pollMinutes").GetInt32().ShouldBe(0);
        off.GetProperty("nextPollAt").ValueKind.ShouldBe(JsonValueKind.Null);
        await git.CommitAsync("shop", Files(("README.md", "x")), "Not seen");
        (await PollAsync(factory, due: false)).Started.ShouldBeEmpty();

        (await root.PatchAsJsonAsync($"/api/v1/repositories/{repository}", new { pollMinutes = 10 }, Cancel)).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Repositories.ExecuteUpdateAsync(set => set.SetProperty(r => r.Url, git.Url("gone")), Cancel);
        }

        (await PollAsync(factory)).Skipped.ShouldHaveSingleItem().ShouldContain("Polling the repository 'shop' failed");
        var failed = await (await root.GetAsync(new Uri($"/api/v1/repositories/{repository}", UriKind.Relative), Cancel)).JsonAsync();
        failed.GetProperty("lastError").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_repository_without_recorded_refs_records_them_on_its_first_poll_without_runs()
    {
        await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build)), "Start");
        await using var factory = Factory();
        var (root, _, folder) = await ConnectAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().RepositoryRefs.ExecuteDeleteAsync(Cancel);
        }

        await git.CommitAsync("shop", Files(("README.md", "x")), "Before the first poll");
        await PollAsync(factory);

        (await RunsAsync(factory, await AutomationIdAsync(root, folder, "build"))).ShouldBeEmpty();
        await using var check = factory.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().RepositoryRefs.CountAsync(Cancel)).ShouldBe(1);
    }

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database).With("Repositories:CacheDirectory", cache);

    private static Dictionary<string, string?> Files(params (string Path, string? Content)[] files) =>
        files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

    private async Task<(HttpClient Root, Guid Repository, Guid Folder)> ConnectAsync(AutoCanvasFactory factory)
    {
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var folder = (await (await root.PostAsJsonAsync("/api/v1/folders", new { name = "Shop" }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        var connected = await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = git.Url("shop").ToString() }, Cancel);
        connected.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (root, (await connected.JsonAsync()).GetProperty("id").GetGuid(), folder);
    }

    private static async Task<Guid> AutomationIdAsync(HttpClient client, Guid folder, string name) =>
        (await (await client.GetAsync(new Uri($"/api/v1/folders/{folder}/automations", UriKind.Relative), Cancel)).JsonAsync())
            .GetProperty("items").EnumerateArray().Single(a => string.Equals(a.GetProperty("name").GetString(), name, StringComparison.Ordinal)).GetProperty("id").GetGuid();

    /// <summary>Polls the repositories, made due first unless <paramref name="due"/> is false.</summary>
    private static async Task<TriggerRound> PollAsync(AutoCanvasFactory factory, bool due = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (due)
        {
            await services.GetRequiredService<AutoCanvasDbContext>().Repositories.Where(r => r.PollMinutes > 0).ExecuteUpdateAsync(set => set.SetProperty(r => r.NextPollAt, (DateTimeOffset?)null), Cancel);
        }

        return await services.GetRequiredService<RepositoryPolling>().PollDueAsync(Cancel);
    }

    private static async Task<List<Run>> RunsAsync(AutoCanvasFactory factory, Guid automation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Runs.AsNoTracking().Where(r => r.AutomationId == automation).OrderBy(r => r.Number).ToListAsync(Cancel);
    }
}

public sealed class RepositoryPushTestsOnSqlite() : RepositoryPushTests(DatabaseProvider.Sqlite);

public sealed class RepositoryPushTestsOnPostgres() : RepositoryPushTests(DatabaseProvider.Postgres);
