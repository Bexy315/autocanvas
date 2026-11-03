using System.Net;
using System.Net.Http.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Variables;
using AutoCanvas.Server.Setup;
using AutoCanvas.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Server.Tests;

public abstract class SetupApiTests(DatabaseProvider provider) : DatabaseTest(provider), IDisposable
{
    private const string TokenHeader = "X-AutoCanvas-Setup-Token";

    private readonly string directory = Directory.CreateTempSubdirectory("autocanvas-setup-").FullName;

    private string RunnerTokenFile => Path.Combine(directory, "handoff", "runner-token");

    private string KeyFile => Path.Combine(directory, "data", "keys", "master.key");

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A fresh installation: no account, no configured master key, a bundled runner.</summary>
    private AutoCanvasFactory Fresh() => new AutoCanvasFactory().WithDatabase(Database)
        .With("Setup:Enabled", "true")
        .With("Setup:RunnerTokenFile", RunnerTokenFile)
        .With("Secrets:MasterKey", null)
        .With("Server:DataDirectory", Path.Combine(directory, "data"));

    private static object Request(string password = ApiClient.Password) => new
    {
        admin = new { userName = "root", displayName = "Root", password },
        publicAddress = "http://autocanvas.example.org",
        runnerAddress = "http://autocanvas.example.org:5081",
        bundledRunner = true,
        example = true,
    };

    private static HttpRequestMessage WithToken(HttpMethod method, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri("/api/v1/setup", UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Add(TokenHeader, token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Fact]
    public async Task A_fresh_installation_answers_only_its_setup_until_someone_with_the_token_sets_it_up()
    {
        await using var factory = Fresh();
        var client = factory.CreateApiClient();
        var token = factory.Services.GetRequiredService<SetupState>().Token;

        var blocked = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Cancel);
        blocked.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await blocked.ProblemTypeAsync()).ShouldBe("setup-required");
        (await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var anonymous = await (await client.SendAsync(WithToken(HttpMethod.Get, null), Cancel)).JsonAsync();
        anonymous.GetProperty("open").GetBoolean().ShouldBeTrue();
        anonymous.GetProperty("details").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await client.SendAsync(WithToken(HttpMethod.Get, "wrong"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var details = (await (await client.SendAsync(WithToken(HttpMethod.Get, token), Cancel)).JsonAsync()).GetProperty("details");
        details.GetProperty("masterKey").GetString().ShouldBe("Generated");
        details.GetProperty("bundledRunner").GetBoolean().ShouldBeTrue();
        details.GetProperty("publicAddress").GetProperty("source").GetString().ShouldBe("None");

        // Without the token, or with a password the policy refuses, nothing is created.
        (await client.SendAsync(WithToken(HttpMethod.Post, null, Request()), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var refused = await client.SendAsync(WithToken(HttpMethod.Post, token, Request(password: "short")), Cancel);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.JsonAsync()).GetProperty("field").GetString().ShouldBe("admin.password");

        var done = await client.SendAsync(WithToken(HttpMethod.Post, token, Request()), Cancel);
        done.StatusCode.ShouldBe(HttpStatusCode.OK);
        var completed = await done.JsonAsync();
        completed.GetProperty("me").GetProperty("user").GetProperty("isAdmin").GetBoolean().ShouldBeTrue();
        completed.GetProperty("masterKey").GetProperty("key").GetString().ShouldBe((await File.ReadAllTextAsync(KeyFile, Cancel)).Trim());
        completed.GetProperty("runnerTokenWritten").GetBoolean().ShouldBeTrue();
        (await File.ReadAllTextAsync(RunnerTokenFile, Cancel)).ShouldStartWith("acrt_");

        // Signed in as the new admin, with the addresses and the example in place.
        (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var instance = await (await client.GetAsync(new Uri("/api/v1/instance", UriKind.Relative), Cancel)).JsonAsync();
        instance.GetProperty("publicAddress").GetProperty("value").GetString().ShouldBe("http://autocanvas.example.org/");
        instance.GetProperty("runnerAddress").GetProperty("source").GetString().ShouldBe("Database");
        var example = completed.GetProperty("exampleAutomationId").GetGuid();
        (await client.GetAsync(new Uri($"/api/v1/automations/{example}", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Over for good.
        (await (await client.SendAsync(WithToken(HttpMethod.Get, token), Cancel)).JsonAsync()).GetProperty("open").GetBoolean().ShouldBeFalse();
        (await client.SendAsync(WithToken(HttpMethod.Post, token, Request()), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Setup_mode_does_not_open_for_a_bootstrapped_installation_and_ends_when_an_account_appears()
    {
        await using (var bootstrapped = Fresh().With("Bootstrap:AdminUserName", "root").With("Bootstrap:AdminPassword", ApiClient.Password))
        {
            var client = bootstrapped.CreateApiClient();
            (await (await client.GetAsync(new Uri("/api/v1/setup", UriKind.Relative), Cancel)).JsonAsync()).GetProperty("open").GetBoolean().ShouldBeFalse();
            (await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        await using var database = await TestDatabase.CreateAsync(Provider);
        await using var fresh = new AutoCanvasFactory().WithDatabase(database).With("Setup:Enabled", "true");
        var other = fresh.CreateApiClient();
        (await other.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        // As 'admin create' would, from another process.
        await fresh.CreateUserAsync("root", isAdmin: true);
        (await other.GetAsync(new Uri("/api/v1/me", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admins_change_the_addresses_unless_the_configuration_sets_them()
    {
        await using var factory = new AutoCanvasFactory().WithDatabase(Database).With("Server:PublicUrl", "http://configured.example.org");
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var vic = await factory.SignedInAsync("vic");

        var changed = await root.PutAsJsonAsync("/api/v1/instance", new { publicAddress = (string?)null, runnerAddress = "http://runners.example.org:5081" }, Cancel);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var view = await changed.JsonAsync();
        view.GetProperty("publicAddress").GetProperty("source").GetString().ShouldBe("Configuration");
        view.GetProperty("runnerAddress").GetProperty("value").GetString().ShouldBe("http://runners.example.org:5081/");

        var locked = await root.PutAsJsonAsync("/api/v1/instance", new { publicAddress = "https://other.example.org", runnerAddress = (string?)null }, Cancel);
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await locked.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("Server:PublicUrl");
        (await root.PutAsJsonAsync("/api/v1/instance", new { publicAddress = (string?)null, runnerAddress = "ftp://x/path" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await vic.GetAsync(new Uri("/api/v1/instance", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var audit = await (await root.GetAsync(new Uri("/api/v1/audit?action=instance.updated", UriKind.Relative), Cancel)).JsonAsync();
        audit.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_generated_master_key_is_kept_for_the_next_start_but_never_replaces_the_key_of_a_used_database()
    {
        string key;
        await using (var first = Fresh())
        {
            _ = first.CreateClient();
            key = (await File.ReadAllTextAsync(KeyFile, Cancel)).Trim();
            MasterKey.Parse(key, out _).ShouldNotBeNull();
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(KeyFile).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        await using (var again = Fresh())
        {
            (await again.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await File.ReadAllTextAsync(KeyFile, Cancel)).Trim().ShouldBe(key);
        }

        // A database set up with a configured key does not get a new one generated for it.
        await using var used = await TestDatabase.CreateAsync(Provider);
        await using (var configured = new AutoCanvasFactory().WithDatabase(used))
        {
            _ = configured.CreateClient();
        }

        var elsewhere = Path.Combine(directory, "elsewhere");
        var message = await ServerStart.FailsAsync<InvalidOperationException>(used, ("Secrets:MasterKey", null), ("Server:DataDirectory", elsewhere));
        message.ShouldContain("No master key is configured");
        File.Exists(Path.Combine(elsewhere, "keys", "master.key")).ShouldBeFalse();
    }
}

[Collection(CommandLineTests.Name)]
public sealed class SetupApiTestsOnSqlite() : SetupApiTests(DatabaseProvider.Sqlite);

[Collection(CommandLineTests.Name)]
public sealed class SetupApiTestsOnPostgres() : SetupApiTests(DatabaseProvider.Postgres);
