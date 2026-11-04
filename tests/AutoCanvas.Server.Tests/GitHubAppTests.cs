using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Forges;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Application.Runs;
using AutoCanvas.Server.Application.Variables;
using AutoCanvas.Server.Configuration;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoCanvas.Server.Tests;

public abstract class GitHubAppTests(DatabaseProvider provider) : DatabaseTest(provider), IAsyncLifetime
{
    private const string Build = """
        version: 1
        triggers:
          manual: true
          push:
            branches: [main]
        jobs:
          build:
            steps:
              - run: make
        """;

    private const long ShopId = 9001;

    private static readonly string[] Readme = ["README.md"];

    private readonly string cache = Directory.CreateTempSubdirectory("autocanvas-repository-cache-").FullName;
    private GitHubStub github = null!;

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        github = await GitHubStub.StartAsync();
        await github.Git.CreateAsync("shop");
        github.Install(7, "acme", (ShopId, "shop"));
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
    public async Task The_manifest_needs_the_public_address_and_leads_to_GitHub_with_the_events_address()
    {
        await using (var bare = Factory(publicAddress: null))
        {
            var admin = await bare.SignedInAsync("first", isAdmin: true);
            (await admin.PostAsJsonAsync("/api/v1/forges/github/manifest", new { }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var ada = await factory.SignedInAsync("ada");

        var manifest = await (await root.PostAsJsonAsync("/api/v1/forges/github/manifest", new { organization = "acme" }, Cancel)).JsonAsync();

        manifest.GetProperty("action").GetString()!.ShouldStartWith("https://github.com/organizations/acme/settings/apps/new?state=");
        var body = JsonDocument.Parse(manifest.GetProperty("manifest").GetString()!).RootElement;
        body.GetProperty("hook_attributes").GetProperty("url").GetString().ShouldBe("http://autocanvas.test/api/v1/forges/github/events");
        body.GetProperty("redirect_url").GetString().ShouldBe("http://autocanvas.test/admin/forges/github/created");
        body.GetProperty("default_permissions").GetProperty("contents").GetString().ShouldBe("write");
        body.GetProperty("default_events").EnumerateArray().Select(e => e.GetString()).ShouldBe(["push", "pull_request"]);
        (await root.PostAsJsonAsync("/api/v1/forges/github/manifest", new { organization = "no spaces" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ada.PostAsJsonAsync("/api/v1/forges/github/manifest", new { }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ada.GetAsync(new Uri("/api/v1/forges/github", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_launch_page_posts_the_manifest_to_GitHub_under_a_policy_of_its_own()
    {
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var manifest = await (await root.PostAsJsonAsync("/api/v1/forges/github/manifest", new { organization = "acme", webAddress = github.Web.ToString() }, Cancel)).JsonAsync();
        var state = manifest.GetProperty("state").GetString()!;

        // A plain form post, as the browser makes it: no X-AutoCanvas-Request header.
        using var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("state", state)]);
        var launched = await root.PostAsync(new Uri("/api/v1/forges/github/manifest/launch", UriKind.Relative), form, Cancel);

        launched.StatusCode.ShouldBe(HttpStatusCode.OK);
        launched.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var policy = launched.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain($"form-action {github.Web.GetLeftPart(UriPartial.Authority)}");
        policy.ShouldContain("script-src 'sha256-");
        var html = await launched.Content.ReadAsStringAsync(Cancel);
        html.ShouldContain($"action=\"{System.Net.WebUtility.HtmlEncode(manifest.GetProperty("action").GetString()!)}\"");
        html.ShouldContain("name=\"manifest\"");
        html.ShouldContain("&quot;redirect_url&quot;");

        using var forged = new FormUrlEncodedContent([new KeyValuePair<string, string>("state", "forged")]);
        var refused = await root.PostAsync(new Uri("/api/v1/forges/github/manifest/launch", UriKind.Relative), forged, Cancel);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(Cancel)).ShouldContain("Back to Forges");
        var ada = await factory.SignedInAsync("ada");
        using var again = new FormUrlEncodedContent([new KeyValuePair<string, string>("state", state)]);
        (await ada.PostAsync(new Uri("/api/v1/forges/github/manifest/launch", UriKind.Relative), again, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_code_GitHub_hands_back_becomes_the_app_whose_secrets_are_stored_encrypted()
    {
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        (await (await root.GetAsync(new Uri("/api/v1/forges/github", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("app").ValueKind.ShouldBe(JsonValueKind.Null);
        var state = (await (await root.PostAsJsonAsync("/api/v1/forges/github/manifest", new { webAddress = github.Web.ToString() }, Cancel)).JsonAsync()).GetProperty("state").GetString()!;

        (await root.PostAsJsonAsync("/api/v1/forges/github/created", new { code = GitHubStub.ManifestCode, state = "forged" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await root.PostAsJsonAsync("/api/v1/forges/github/created", new { code = "unknown", state }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var created = await root.PostAsJsonAsync("/api/v1/forges/github/created", new { code = GitHubStub.ManifestCode, state }, Cancel);

        created.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await created.Content.ReadAsStringAsync(Cancel);
        text.ShouldNotContain(github.WebhookSecret);
        var app = JsonDocument.Parse(text).RootElement.GetProperty("app");
        app.GetProperty("appId").GetInt64().ShouldBe(GitHubStub.AppId);
        app.GetProperty("slug").GetString().ShouldBe("autocanvas-test");
        app.GetProperty("apiAddress").GetString().ShouldBe(new Uri(github.Web, "api/v3").ToString());
        app.GetProperty("webhookUrl").GetString().ShouldBe("http://autocanvas.test/api/v1/forges/github/events");
        app.GetProperty("installUrl").GetString()!.ShouldEndWith("/apps/autocanvas-test/installations/new");
        app.GetProperty("installations").EnumerateArray().Single().GetProperty("account").GetString().ShouldBe("acme");
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().GitHubApps.SingleAsync(Cancel);
        stored.PrivateKeyCiphertext.ShouldNotContain("PRIVATE KEY");
        (await root.PostAsJsonAsync("/api/v1/forges/github/manifest", new { }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_existing_app_is_entered_by_hand_and_GitHub_checks_its_key()
    {
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        using var other = System.Security.Cryptography.RSA.Create(2048);

        var wrong = await root.PutAsJsonAsync("/api/v1/forges/github", new { appId = GitHubStub.AppId, privateKey = other.ExportRSAPrivateKeyPem(), webhookSecret = "x", webAddress = github.Web.ToString() }, Cancel);
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrong.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("does not take the app id and key");
        (await root.PutAsJsonAsync("/api/v1/forges/github", new { appId = GitHubStub.AppId, privateKey = "not a key", webhookSecret = "x", webAddress = github.Web.ToString() }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await ConfigureAsync(root);

        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().AuditEvents.CountAsync(e => e.Action == "github.app_configured", Cancel)).ShouldBe(1);
    }

    [Fact]
    public async Task A_repository_from_an_installation_is_connected_read_and_checked_out_with_installation_tokens()
    {
        await github.Git.CommitAsync("shop", new Dictionary<string, string?>(StringComparer.Ordinal) { [".autocanvas/automations/build.yml"] = Build }, "Start");
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        await ConfigureAsync(root);
        var folder = await FolderAsync(root);

        var listed = await (await root.GetAsync(new Uri($"/api/v1/folders/{folder}/github/repositories", UriKind.Relative), Cancel)).JsonAsync();
        var shop = listed.EnumerateArray().ShouldHaveSingleItem();
        (shop.GetProperty("fullName").GetString(), shop.GetProperty("installationId").GetInt64()).ShouldBe(("acme/shop", 7L));
        (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories/github", new { installationId = 7, repository = "acme/other" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var connected = await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories/github", new { installationId = 7, repository = "acme/shop" }, Cancel);

        connected.StatusCode.ShouldBe(HttpStatusCode.Created);
        var repository = await connected.JsonAsync();
        (repository.GetProperty("kind").GetString(), repository.GetProperty("name").GetString(), repository.GetProperty("automations").GetInt32()).ShouldBe(("GitHub", "shop", 1));
        repository.GetProperty("hasCredential").GetBoolean().ShouldBeFalse();
        var id = repository.GetProperty("id").GetGuid();
        (await root.PatchAsJsonAsync($"/api/v1/repositories/{id}", new { secret = "mine" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await root.PostAsync(new Uri($"/api/v1/repositories/{id}/sync", UriKind.Relative), null, Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A run checks out with the installation token, which the server masks; tokens are reused while valid.
        var automation = (await (await root.GetAsync(new Uri($"/api/v1/folders/{folder}/automations", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();
        var run = (await (await root.PostAsync(new Uri($"/api/v1/automations/{automation}/runs", UriKind.Relative), null, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var attempt = await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().JobAttempts.AsNoTracking().Where(a => a.Job!.RunId == run).Select(a => a.Id).SingleAsync(Cancel);
        var checkout = (await scope.ServiceProvider.GetRequiredService<JobOfferBuilder>().BuildAsync(attempt, Cancel)).Value!.Checkouts.ShouldHaveSingleItem();
        (checkout.Url, checkout.UserName, checkout.Password).ShouldBe((github.Git.Url("shop"), "x-access-token", github.Token));
        var redactor = await scope.ServiceProvider.GetRequiredService<LogIngestion>().RedactorAsync(attempt, Cancel);
        redactor!.Redact($"token {github.Token}").ShouldNotContain(github.Token);
        github.TokenRequests.ShouldBe(1);

        // The app goes only once its repositories have.
        (await root.DeleteAsync(new Uri("/api/v1/forges/github", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await root.DeleteAsync(new Uri($"/api/v1/repositories/{id}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await root.DeleteAsync(new Uri("/api/v1/forges/github", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_apps_events_bring_pushes_once_per_delivery_and_mark_uninstalled_repositories()
    {
        var start = await github.Git.CommitAsync("shop", new Dictionary<string, string?>(StringComparer.Ordinal) { [".autocanvas/automations/build.yml"] = Build }, "Start");
        await using var factory = Factory();
        var anonymous = factory.CreateClient();
        (await SendAsync(anonymous, "ping", "d0", "{}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var root = await factory.SignedInAsync("root", isAdmin: true);
        await ConfigureAsync(root);
        var folder = await FolderAsync(root);
        var repository = (await (await root.PostAsJsonAsync($"/api/v1/folders/{folder}/repositories/github", new { installationId = 7, repository = "acme/shop", pollMinutes = 0 }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();
        var pushed = await github.Git.CommitAsync("shop", new Dictionary<string, string?>(StringComparer.Ordinal) { ["README.md"] = "x" }, "Readme");
        var push = JsonSerializer.Serialize(new
        {
            @ref = "refs/heads/main",
            before = start,
            after = pushed,
            repository = new { id = ShopId, full_name = "acme/shop" },
            pusher = new { name = "octocat" },
            head_commit = new { id = pushed, message = "Readme" },
            commits = new[] { new { id = pushed, added = Readme } },
        });

        (await SendAsync(anonymous, "push", "d1", push, signature: "sha256=00")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await (await SendAsync(anonymous, "ping", "d2", "{}")).JsonAsync()).GetProperty("message").GetString().ShouldBe("pong");
        (await (await SendAsync(anonymous, "push", "d3", push)).JsonAsync()).GetProperty("queued").GetBoolean().ShouldBeTrue();
        (await (await SendAsync(anonymous, "push", "d3", push)).JsonAsync()).GetProperty("queued").GetBoolean().ShouldBeFalse();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RepositoryPolling>().HandleReceivedAsync(Cancel);
            var run = await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().Runs.AsNoTracking().SingleAsync(Cancel);
            (run.TriggeredByName, run.CommitSha).ShouldBe(("octocat", pushed));
        }

        var uninstalled = JsonSerializer.Serialize(new { action = "deleted", installation = new { id = 7 } });
        (await SendAsync(anonymous, "installation", "d4", uninstalled)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await root.GetAsync(new Uri($"/api/v1/repositories/{repository}", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("lastError").GetString()!.ShouldContain("uninstalled");
    }

    [Fact]
    public async Task A_rotated_master_key_carries_the_apps_secrets_along()
    {
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        await ConfigureAsync(root);
        await using var scope = factory.Services.CreateAsyncScope();
        var newKey = MasterKey.Generate();

        (await scope.ServiceProvider.GetRequiredService<MasterKeyGuard>().RotateAsync(newKey, Cancel)).Error.ShouldBeNull();

        var stored = await scope.ServiceProvider.GetRequiredService<AutoCanvasDbContext>().GitHubApps.AsNoTracking().SingleAsync(Cancel);
        new SecretCipher(newKey).Decrypt(GitHubAccess.SecretScope, "private-key", stored.PrivateKeyCiphertext).ShouldBe(github.PrivateKey);
        new SecretCipher(newKey).Decrypt(GitHubAccess.SecretScope, "webhook-secret", stored.WebhookSecretCiphertext).ShouldBe(github.WebhookSecret);
    }

    /// <summary>The server, with a public address unless told otherwise, and without its background handling of pushes.</summary>
    private AutoCanvasFactory Factory(string? publicAddress = "http://autocanvas.test")
    {
        var factory = new AutoCanvasFactory().WithDatabase(Database).With("Repositories:CacheDirectory", cache).WithServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(RepositoryPollService)).ToList())
            {
                services.Remove(descriptor);
            }
        });
        return publicAddress is null ? factory : factory.With("Server:PublicUrl", publicAddress);
    }

    private async Task ConfigureAsync(HttpClient root)
    {
        var configured = await root.PutAsJsonAsync("/api/v1/forges/github", new { appId = GitHubStub.AppId, privateKey = github.PrivateKey, webhookSecret = github.WebhookSecret, webAddress = github.Web.ToString() }, Cancel);
        configured.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await configured.JsonAsync()).GetProperty("app").GetProperty("ownerLogin").GetString().ShouldBe("acme");
    }

    private static async Task<Guid> FolderAsync(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Shop" }, Cancel)).JsonAsync()).GetProperty("id").GetGuid();

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, string eventName, string delivery, string body, string? signature = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/forges/github/events") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-GitHub-Event", eventName);
        request.Headers.Add("X-GitHub-Delivery", delivery);
        request.Headers.Add("X-Hub-Signature-256", signature ?? github.Sign(body));
        return await client.SendAsync(request, Cancel);
    }
}

public sealed class GitHubAppTestsOnSqlite() : GitHubAppTests(DatabaseProvider.Sqlite);

public sealed class GitHubAppTestsOnPostgres() : GitHubAppTests(DatabaseProvider.Postgres);
