using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Configuration;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoCanvas.Server.Tests;

public abstract class GitHubChangeProposalTests(DatabaseProvider provider) : DatabaseTest(provider), IAsyncLifetime
{
    private const string Build = """
        version: 1
        triggers:
          manual: true
        jobs:
          build:
            steps:
              - run: make
        """;

    private const string Path = ".autocanvas/automations/build.yml";

    private readonly string cache = Directory.CreateTempSubdirectory("autocanvas-repository-cache-").FullName;
    private GitHubStub github = null!;
    private string commit = null!;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        github = await GitHubStub.StartAsync();
        await github.Git.CreateAsync("acme/shop");
        github.Install(7, "acme", (9001, "shop"));
        commit = await github.Git.CommitAsync("acme/shop", new Dictionary<string, string?>(StringComparer.Ordinal) { [Path] = Build }, "Start");
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
    public async Task A_change_becomes_a_branch_from_the_versions_commit_and_a_pull_request()
    {
        await using var factory = Factory();
        var (root, folder) = await SetUpAsync(factory);
        var automation = await AutomationAsync(root, folder);
        var changed = Build.Replace("make", "make -j8", StringComparison.Ordinal);

        var proposed = await root.PostAsJsonAsync($"/api/v1/automations/{automation}/pull-request", new { yaml = changed, title = "Faster build" }, Cancel);

        proposed.StatusCode.ShouldBe(HttpStatusCode.Created);
        var view = await proposed.JsonAsync();
        view.GetProperty("number").GetInt32().ShouldBe(41);
        view.GetProperty("url").GetString().ShouldBe("https://github.com/acme/shop/pull/41");
        var branch = view.GetProperty("branch").GetString()!;
        branch.ShouldStartWith("autocanvas/build-");
        github.Branches.ShouldHaveSingleItem().ShouldBe((branch, commit));
        var change = github.FileChanges.ShouldHaveSingleItem();
        (change.Path, change.Branch, change.Sha, change.Content, change.Message).ShouldBe((Path, branch, GitHubStub.BlobOf(Path, commit), changed, "Change build in AutoCanvas"));
        var pull = github.PullRequests.ShouldHaveSingleItem();
        (pull.GetProperty("title").GetString(), pull.GetProperty("head").GetString(), pull.GetProperty("base").GetString()).ShouldBe(("Faster build", branch, "main"));
        pull.GetProperty("body").GetString().ShouldBe($"Proposed by root in AutoCanvas: http://autocanvas.test/automations/{automation}");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().AuditEvents.CountAsync(e => e.Action == "automation.pull_request_proposed", Cancel)).ShouldBe(1);

        // The automation itself does not change until the pull request is merged.
        (await (await root.GetAsync(new Uri($"/api/v1/automations/{automation}", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("yaml").GetString().ShouldBe(Build);
    }

    [Fact]
    public async Task Nothing_new_an_invalid_file_and_a_refusing_GitHub_are_said_so()
    {
        await using var factory = Factory();
        var (root, folder) = await SetUpAsync(factory);
        var automation = await AutomationAsync(root, folder);
        var address = $"/api/v1/automations/{automation}/pull-request";

        (await (await root.PostAsJsonAsync(address, new { yaml = Build }, Cancel)).JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("Nothing changed");
        var invalid = await root.PostAsJsonAsync(address, new { yaml = "version: 1\njobs: {}\n" }, Cancel);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.JsonAsync()).GetProperty("diagnostics").GetArrayLength().ShouldBeGreaterThan(0);
        github.RefusePullRequests = true;
        var refused = await root.PostAsJsonAsync(address, new { yaml = Build + "# more\n" }, Cancel);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("GitHub did not take the change: GitHub answered 422: Validation Failed");
        var stranger = await factory.SignedInAsync("stranger");
        (await stranger.PostAsJsonAsync(address, new { yaml = Build + "# more\n" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Managed_and_Generic_Git_automations_cannot_be_proposed()
    {
        await using var factory = Factory();
        var (root, folder) = await SetUpAsync(factory, github: false);
        var fromGit = await AutomationAsync(root, folder);
        var managed = (await (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/automations", new { name = "managed", yaml = Build }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();

        var git = await root.PostAsJsonAsync($"/api/v1/automations/{fromGit}/pull-request", new { yaml = Build + "# more\n" }, Cancel);
        git.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await git.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("change .autocanvas/automations/build.yml in the repository");
        (await root.PostAsJsonAsync($"/api/v1/automations/{managed}/pull-request", new { yaml = Build + "# more\n" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Repository_automations_runs_and_repositories_link_to_GitHub()
    {
        await using var factory = Factory();
        var (root, folder) = await SetUpAsync(factory);
        var automation = await AutomationAsync(root, folder);
        var web = new Uri(github.Git.Address, "acme/shop").ToString();

        var source = (await (await root.GetAsync(new Uri($"/api/v1/automations/{automation}", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("repository");
        (source.GetProperty("kind").GetString(), source.GetProperty("proposesChanges").GetBoolean()).ShouldBe(("GitHub", true));
        source.GetProperty("webUrl").GetString().ShouldBe(web);
        source.GetProperty("fileUrl").GetString().ShouldBe($"{web}/blob/{commit}/{Path}");
        source.GetProperty("commitUrl").GetString().ShouldBe($"{web}/commit/{commit}");
        var listed = (await (await root.GetAsync(new Uri($"/api/v1/folders/{folder}/automations", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("items")[0].GetProperty("repository");
        (listed.GetProperty("repositoryName").GetString(), listed.GetProperty("kind").GetString()).ShouldBe(("shop", "GitHub"));
        var repositories = await (await root.GetAsync(new Uri($"/api/v1/folders/{folder}/repositories", UriKind.Relative), Cancel)).JsonAsync();
        repositories[0].GetProperty("webUrl").GetString().ShouldBe(web);

        var run = await (await root.PostAsync(new Uri($"/api/v1/automations/{automation}/runs", UriKind.Relative), null, Cancel)).JsonAsync();
        var read = await (await root.GetAsync(new Uri($"/api/v1/runs/{run.GetProperty("id").GetGuid()}", UriKind.Relative), Cancel)).JsonAsync();
        read.GetProperty("commitUrl").GetString().ShouldBe($"{web}/commit/{commit}");
        read.GetProperty("pullRequest").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private AutoCanvasFactory Factory() =>
        new AutoCanvasFactory().WithDatabase(Database).With("Repositories:CacheDirectory", cache).With("Server:PublicUrl", "http://autocanvas.test").WithServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(RepositoryPollService)).ToList())
            {
                services.Remove(descriptor);
            }
        });

    /// <summary>A folder with the repository, through the GitHub App or as Generic Git with the same token.</summary>
    private async Task<(HttpClient Root, Guid Folder)> SetUpAsync(AutoCanvasFactory factory, bool github = true)
    {
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var folder = (await (await root.PostAsJsonAsync("/api/v1/folders", new { name = "Shop" }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        if (github)
        {
            (await root.PutAsJsonAsync("/api/v1/forges/github", new { appId = GitHubStub.AppId, privateKey = this.github.PrivateKey, webhookSecret = this.github.WebhookSecret, webAddress = this.github.Web.ToString() }, Cancel)).EnsureSuccessStatusCode();
            (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories/github", new { installationId = 7, repository = "acme/shop", pollMinutes = 0 }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        else
        {
            (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = this.github.Git.Url("acme/shop").ToString(), secret = this.github.Token, pollMinutes = 0 }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        return (root, folder);
    }

    private static async Task<Guid> AutomationAsync(HttpClient client, Guid folder) =>
        (await (await client.GetAsync(new Uri($"/api/v1/folders/{folder}/automations", UriKind.Relative), Cancel)).JsonAsync())
            .GetProperty("items").EnumerateArray().Single(a => string.Equals(a.GetProperty("name").GetString(), "build", StringComparison.Ordinal)).GetProperty("id").GetGuid();
}

public sealed class GitHubChangeProposalTestsOnSqlite() : GitHubChangeProposalTests(DatabaseProvider.Sqlite);

public sealed class GitHubChangeProposalTestsOnPostgres() : GitHubChangeProposalTests(DatabaseProvider.Postgres);
