using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AutoCanvas.Server.Tests;

/// <summary>
/// The "S1 done when" walk-through of <c>docs/plans/s1.md</c>, end to end over HTTP on each
/// database: every response body and every log line is recorded, and the secret's value must be in
/// none of them.
/// </summary>
public abstract class SmokeTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private const string Initial = "initial password 123";
    private const string Hello = """
        version: 1
        name: Say hello
        jobs:
          hello:
            steps:
              - run: echo hello
        """;

    private readonly ConcurrentQueue<string> responses = new();

    [Fact]
    public async Task The_first_milestone_works_end_to_end_without_leaking_the_secret()
    {
        var secret = $"smoke-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";
        using var logs = new RecordingLoggerFactory();
        await using var factory = new AutoCanvasFactory().WithDatabase(Database)
            .With("Bootstrap:AdminUserName", "root")
            .With("Bootstrap:AdminPassword", Initial)
            .WithServices(services => services.AddSingleton<ILoggerFactory>(logs));

        // The first admin comes from the settings and must change the password at the first sign-in.
        var root = await SignInAsync(factory, "root");
        var token = (await ReadAsync(await root.PostAsJsonAsync("/api/v1/me/tokens", new { name = "smoke" }, Cancel), HttpStatusCode.Created)).GetProperty("token").GetString()!;
        var admin = Client(factory, csrf: false);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var eddieId = await CreateUserAsync(admin, "eddie");
        var vicId = await CreateUserAsync(admin, "vic");
        var viewers = Id(await ReadAsync(await admin.PostAsJsonAsync("/api/v1/groups", new { name = "viewers" }, Cancel), HttpStatusCode.Created));
        await ReadAsync(await admin.PutAsync(Uri($"/api/v1/groups/{viewers}/members/{vicId}"), null, Cancel), HttpStatusCode.NoContent);

        var folder = Id(await ReadAsync(await root.PostAsJsonAsync("/api/v1/folders", new { name = "Team" }, Cancel), HttpStatusCode.Created));
        await ReadAsync(await root.PutAsJsonAsync($"/api/v1/folders/{folder}/grants", new { groupId = viewers, role = "Viewer" }, Cancel));
        await ReadAsync(await root.PutAsJsonAsync($"/api/v1/folders/{folder}/grants", new { userId = eddieId, role = "Editor" }, Cancel));

        var eddie = await SignInAsync(factory, "eddie");
        var automation = Id(await ReadAsync(await eddie.PostAsJsonAsync($"/api/v1/folders/{folder}/automations", new { name = "hello", yaml = Hello }, Cancel), HttpStatusCode.Created));

        var vic = await SignInAsync(factory, "vic");
        (await ReadAsync(await vic.GetAsync(Uri($"/api/v1/automations/{automation}"), Cancel))).GetProperty("yaml").GetString().ShouldBe(Hello);
        await ReadAsync(await vic.PutAsJsonAsync($"/api/v1/automations/{automation}", new { yaml = Hello + "\n" }, Cancel), HttpStatusCode.Forbidden);

        await ReadAsync(await eddie.PutAsJsonAsync($"/api/v1/folders/{folder}/secrets/DEPLOY_TOKEN", new { value = secret }, Cancel), HttpStatusCode.Created);
        await ReadAsync(await eddie.GetAsync(Uri($"/api/v1/folders/{folder}/secrets"), Cancel));
        await ReadAsync(await vic.GetAsync(Uri($"/api/v1/automations/{automation}/configuration"), Cancel));
        await ReadAsync(await eddie.GetAsync(Uri($"/api/v1/automations/{automation}/configuration"), Cancel));

        var actions = await AuditActionsAsync(admin);

        actions.ShouldContain("user.created", StringComparer.Ordinal);
        actions.ShouldContain("user.password_changed", StringComparer.Ordinal);
        actions.ShouldContain("token.created", StringComparer.Ordinal);
        actions.ShouldContain("group.created", StringComparer.Ordinal);
        actions.ShouldContain("group.member_added", StringComparer.Ordinal);
        actions.ShouldContain("folder.created", StringComparer.Ordinal);
        actions.ShouldContain("folder.grant_set", StringComparer.Ordinal);
        actions.ShouldContain("automation.created", StringComparer.Ordinal);
        actions.ShouldContain("secret.set", StringComparer.Ordinal);
        // The checks below would pass trivially if nothing were recorded: the token, shown once on
        // creation, proves response bodies are seen, and the server's own log lines that logs are.
        responses.Count.ShouldBeGreaterThan(20);
        responses.ShouldContain(body => body.Contains(token, StringComparison.Ordinal));
        logs.Entries.ShouldContain(entry => entry.Category.StartsWith("AutoCanvas.", StringComparison.Ordinal));
        responses.ShouldAllBe(body => !body.Contains(secret, StringComparison.Ordinal));
        logs.Entries.ShouldNotBeEmpty();
        logs.Entries.ShouldAllBe(entry => !entry.Message.Contains(secret, StringComparison.Ordinal));
    }

    private static async Task<HashSet<string>> AuditActionsAsync(HttpClient admin)
    {
        var actions = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = await ReadAsync(await admin.GetAsync(Uri($"/api/v1/audit?limit=200{(cursor is null ? string.Empty : $"&cursor={cursor}")}"), Cancel));
            foreach (var entry in page.GetProperty("items").EnumerateArray())
            {
                actions.Add(entry.GetProperty("action").GetString()!);
            }

            cursor = page.GetProperty("next").GetString();
        }
        while (cursor is not null);

        return actions;
    }

    private static async Task<Guid> CreateUserAsync(HttpClient admin, string userName) =>
        Id(await ReadAsync(await admin.PostAsJsonAsync("/api/v1/users", new { userName, password = Initial }, Cancel), HttpStatusCode.Created));

    /// <summary>Signs in with the one-time password, changes it as required, and signs in again.</summary>
    private async Task<HttpClient> SignInAsync(AutoCanvasFactory factory, string userName)
    {
        var client = Client(factory, csrf: true);
        var password = $"{userName} long password";
        await ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/sign-in", new { userName, password = Initial }, Cancel));
        await ReadAsync(await client.PutAsJsonAsync("/api/v1/me/password", new { currentPassword = Initial, newPassword = password }, Cancel), HttpStatusCode.NoContent);
        await ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/sign-in", new { userName, password }, Cancel));
        return client;
    }

    /// <summary>A client with its own cookies whose response bodies are all recorded.</summary>
    private HttpClient Client(AutoCanvasFactory factory, bool csrf)
    {
        var client = factory.CreateDefaultClient(new RecordingHandler(responses), new CookieContainerHandler());
        if (csrf)
        {
            client.DefaultRequestHeaders.Add("X-AutoCanvas-Request", "1");
        }

        return client;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Cancel));
        return response.Content.Headers.ContentLength == 0 ? default : await response.JsonAsync();
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private sealed class RecordingHandler(ConcurrentQueue<string> bodies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            bodies.Enqueue(string.Concat(response.Headers, response.Content.Headers, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
            return response;
        }
    }
}

public sealed class SmokeTestsOnSqlite() : SmokeTests(DatabaseProvider.Sqlite);

public sealed class SmokeTestsOnPostgres() : SmokeTests(DatabaseProvider.Postgres);
