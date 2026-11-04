using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Variables;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Server.Tests;

public abstract class RepositoryApiTests(DatabaseProvider provider) : DatabaseTest(provider), IAsyncLifetime
{
    private const string Build = """
        version: 1
        name: Build the shop
        triggers:
          manual: true
        jobs:
          build:
            steps:
              - run: make
        """;

    private const string Broken = """
        version: 1
        jobs: {}
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

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database).With("Repositories:CacheDirectory", cache);

    private static Dictionary<string, string?> Files(params (string Path, string? Content)[] files) =>
        files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

    private static async Task<Guid> FolderAsync(HttpClient client, string name = "Shop") =>
        (await (await client.PostAsJsonAsync("/api/v1/folders", new { name }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();

    private static async Task<JsonElement> AutomationsAsync(HttpClient client, Guid folder) =>
        (await (await client.GetAsync(new Uri($"/api/v1/folders/{folder}/automations", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("items");

    private static async Task<JsonElement> SyncAsync(HttpClient client, Guid repository)
    {
        var synced = await client.PostAsync(new Uri($"/api/v1/repositories/{repository}/sync", UriKind.Relative), null, Cancel);
        synced.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await synced.JsonAsync();
    }

    [Fact]
    public async Task A_connected_repository_brings_its_automations_and_follows_its_commits()
    {
        var first = await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build), (".autocanvas/automations/broken.yml", Broken), ("README.md", "shop")), "Add the build");
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var folder = await FolderAsync(root);

        var connected = await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = git.Url("shop").ToString() }, Cancel);
        connected.StatusCode.ShouldBe(HttpStatusCode.Created, await connected.Content.ReadAsStringAsync(Cancel));
        var repository = await connected.JsonAsync();
        var id = repository.GetProperty("id").GetGuid();
        repository.GetProperty("name").GetString().ShouldBe("shop");
        repository.GetProperty("kind").GetString().ShouldBe("GenericGit");
        repository.GetProperty("branch").GetString().ShouldBe("main");
        repository.GetProperty("lastCommit").GetString().ShouldBe(first);
        repository.GetProperty("automations").GetInt32().ShouldBe(1);
        var problem = repository.GetProperty("problems").EnumerateArray().Single();
        problem.GetProperty("path").GetString().ShouldBe(".autocanvas/automations/broken.yml");

        // The file became an automation of the folder, read-only from the repository.
        var listed = (await AutomationsAsync(root, folder)).EnumerateArray().Single();
        listed.GetProperty("name").GetString().ShouldBe("build");
        listed.GetProperty("repository").GetProperty("repositoryName").GetString().ShouldBe("shop");
        var automation = listed.GetProperty("id").GetGuid();
        var view = await (await root.GetAsync(new Uri($"/api/v1/automations/{automation}", UriKind.Relative), Cancel)).JsonAsync();
        view.GetProperty("title").GetString().ShouldBe("Build the shop");
        view.GetProperty("repository").GetProperty("commit").GetString().ShouldBe(first);
        view.GetProperty("repository").GetProperty("path").GetString().ShouldBe(".autocanvas/automations/build.yml");

        // A new commit: the changed file is a new version at it, the fixed one an automation.
        var second = await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build.Replace("make", "make all", StringComparison.Ordinal)), (".autocanvas/automations/broken.yml", Build)), "Build everything", author: "Grace Hopper");
        var synced = await SyncAsync(root, id);
        synced.GetProperty("lastCommit").GetString().ShouldBe(second);
        synced.GetProperty("problems").GetArrayLength().ShouldBe(0);
        synced.GetProperty("automations").GetInt32().ShouldBe(2);
        var versions = await (await root.GetAsync(new Uri($"/api/v1/automations/{automation}/versions", UriKind.Relative), Cancel)).JsonAsync();
        var newest = versions.GetProperty("items")[0];
        newest.GetProperty("number").GetInt32().ShouldBe(2);
        newest.GetProperty("message").GetString().ShouldBe("Build everything");
        newest.GetProperty("createdByName").GetString().ShouldBe("Grace Hopper");

        // Unchanged files make no version; a removed file takes its automation along.
        await git.CommitAsync("shop", Files((".autocanvas/automations/broken.yml", null)), "Drop the second");
        (await SyncAsync(root, id)).GetProperty("automations").GetInt32().ShouldBe(1);
        (await AutomationsAsync(root, folder)).EnumerateArray().Single().GetProperty("currentVersion").GetInt32().ShouldBe(2);

        var audit = await (await root.GetAsync(new Uri("/api/v1/audit?action=repository.*", UriKind.Relative), Cancel)).JsonAsync();
        audit.GetProperty("items").EnumerateArray().ShouldContain(e => string.Equals(e.GetProperty("action").GetString(), "repository.connected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_private_repository_needs_its_credential_which_is_stored_encrypted_and_never_shown()
    {
        await using var guarded = await GitHttpServer.StartAsync(("ci", "s3cret-token"));
        await guarded.CreateAsync("private");
        await guarded.CommitAsync("private", Files((".autocanvas/automations/build.yml", Build)), "Start");
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var folder = await FolderAsync(root);

        var refused = await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = guarded.Url("private").ToString() }, Cancel);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("cannot be read");

        var connected = await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { name = "app", address = guarded.Url("private").ToString(), userName = "ci", secret = "s3cret-token" }, Cancel);
        connected.StatusCode.ShouldBe(HttpStatusCode.Created);
        var text = await connected.Content.ReadAsStringAsync(Cancel);
        text.ShouldNotContain("s3cret-token");
        var repository = JsonDocument.Parse(text).RootElement;
        repository.GetProperty("hasCredential").GetBoolean().ShouldBeTrue();
        repository.GetProperty("automations").GetInt32().ShouldBe(1);
        var id = repository.GetProperty("id").GetGuid();

        // In the database only as ciphertext; a rotated master key carries it along.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Repositories.SingleAsync(r => r.Id == id, Cancel);
            stored.CredentialCiphertext!.ShouldNotContain("s3cret-token");
            var newKey = MasterKey.Generate();
            (await scope.ServiceProvider.GetRequiredService<MasterKeyGuard>().RotateAsync(newKey, Cancel)).Error.ShouldBeNull();
            var rotated = await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Repositories.AsNoTracking().SingleAsync(r => r.Id == id, Cancel);
            rotated.CredentialKeyId.ShouldBe(newKey.Id);
            new SecretCipher(newKey).Decrypt(RepositoryDiscovery.CredentialScope, id.ToString(), rotated.CredentialCiphertext!).ShouldBe("s3cret-token");
        }

        // A wrong credential later: the sync says why and keeps the automations.
        (await root.PatchAsJsonAsync($"/api/v1/repositories/{id}", new { secret = "wrong" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var failed = await (await root.GetAsync(new Uri($"/api/v1/repositories/{id}", UriKind.Relative), Cancel)).JsonAsync();
        failed.GetProperty("lastError").GetString()!.ShouldStartWith("git: ");
        failed.GetProperty("automations").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Folder_roles_decide_who_sees_syncs_and_changes_repositories()
    {
        await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build)), "Start");
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var viewer = await factory.SignedInAsync("vic");
        var operatorClient = await factory.SignedInAsync("olga");
        var folder = await FolderAsync(root);
        foreach (var (user, role) in new[] { ("vic", "Viewer"), ("olga", "Operator") })
        {
            var userId = (await (await root.GetAsync(new Uri("/api/v1/users?limit=100", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("items").EnumerateArray()
                .Single(u => string.Equals(u.GetProperty("userName").GetString(), user, StringComparison.Ordinal)).GetProperty("id").GetGuid();
            (await root.PutAsJsonAsync($"/api/v1/folders/{folder}/grants", new { role, userId }, Cancel)).EnsureSuccessStatusCode();
        }

        var id = (await (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = git.Url("shop").ToString() }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();

        (await viewer.GetAsync(new Uri($"/api/v1/folders/{folder}/repositories", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.PostAsync(new Uri($"/api/v1/repositories/{id}/sync", UriKind.Relative), null, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = git.Url("shop").ToString(), name = "again" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await operatorClient.PostAsync(new Uri($"/api/v1/repositories/{id}/sync", UriKind.Relative), null, Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await operatorClient.DeleteAsync(new Uri($"/api/v1/repositories/{id}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var stranger = await factory.SignedInAsync("stranger");
        (await stranger.GetAsync(new Uri($"/api/v1/repositories/{id}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Names_must_not_clash_and_disconnecting_removes_the_automations()
    {
        await git.CommitAsync("shop", Files((".autocanvas/automations/build.yml", Build), (".autocanvas/automations/deploy.yml", Build)), "Start");
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var folder = await FolderAsync(root);
        (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/automations", new { name = "deploy", yaml = Build }, Cancel)).EnsureSuccessStatusCode();

        var repository = await (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = git.Url("shop").ToString() }, Cancel)).JsonAsync();
        repository.GetProperty("automations").GetInt32().ShouldBe(1);
        repository.GetProperty("problems")[0].GetProperty("message").GetString()!.ShouldContain("another automation named 'deploy'");
        var id = repository.GetProperty("id").GetGuid();
        (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = git.Url("shop").ToString() }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories", new { address = "ftp://example.org/shop.git" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A folder with a repository is not empty.
        (await root.DeleteAsync(new Uri($"/api/v1/folders/{folder}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await root.DeleteAsync(new Uri($"/api/v1/repositories/{id}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AutomationsAsync(root, folder)).EnumerateArray().Select(a => a.GetProperty("name").GetString()).ShouldBe(["deploy"]);
        (await root.GetAsync(new Uri($"/api/v1/repositories/{id}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

public sealed class RepositoryApiTestsOnSqlite() : RepositoryApiTests(DatabaseProvider.Sqlite);

public sealed class RepositoryApiTestsOnPostgres() : RepositoryApiTests(DatabaseProvider.Postgres);
