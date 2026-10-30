using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Tests;

public abstract class TokenApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task A_token_authenticates_its_owner_without_the_request_header()
    {
        await using var factory = Factory();
        var session = await factory.SignedInAsync("root", isAdmin: true);
        var (token, id) = await CreateTokenAsync(session, "ci");
        var client = Bearer(factory, token);

        var me = await client.GetAsync(Uri("/api/v1/me"), Cancel);
        var change = await client.PostAsJsonAsync("/api/v1/groups", new { name = "made-by-token" }, Cancel);

        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.JsonAsync()).GetProperty("user").GetProperty("userName").GetString().ShouldBe("root");
        change.StatusCode.ShouldBe(HttpStatusCode.Created);

        var listed = await (await session.GetAsync(Uri("/api/v1/me/tokens"), Cancel)).JsonAsync();
        var entry = listed.EnumerateArray().Single();
        entry.GetProperty("id").GetGuid().ShouldBe(id);
        entry.ToString().ShouldNotContain(token);
        entry.GetProperty("lastUsedAt").GetDateTimeOffset().ShouldBe(factory.Clock.GetUtcNow());

        var context = Database.CreateContext();
        await using (context)
        {
            var created = await context.AuditEvents.SingleAsync(e => e.Action == "group.created", Cancel);
            created.ActorKind.ShouldBe(AuditActorKind.Token);
            created.ActorName.ShouldBe("root");
        }
    }

    [Fact]
    public async Task Tokens_cannot_manage_tokens_sessions_or_the_password()
    {
        await using var factory = Factory();
        var session = await factory.SignedInAsync("ada");
        var (token, _) = await CreateTokenAsync(session, "ci");
        var client = Bearer(factory, token);

        (await client.GetAsync(Uri("/api/v1/me/tokens"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/v1/me/tokens", new { name = "another" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync(Uri("/api/v1/me/sessions"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync("/api/v1/me/password", new { currentPassword = ApiClient.Password, newPassword = "a different password" }, Cancel))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unknown_expired_and_deleted_tokens_are_unauthorized()
    {
        await using var factory = Factory();
        var session = await factory.SignedInAsync("ada");
        var created = await session.PostAsJsonAsync(
            "/api/v1/me/tokens", new { name = "short", expiresAt = factory.Clock.GetUtcNow().AddHours(1) }, Cancel);
        var shortLived = (await created.JsonAsync()).GetProperty("token").GetString()!;
        var (deleted, deletedId) = await CreateTokenAsync(session, "deleted");

        (await session.DeleteAsync(Uri($"/api/v1/me/tokens/{deletedId}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        factory.Clock.Advance(TimeSpan.FromHours(2));

        foreach (var token in new[] { "acp_unknown", "no-prefix", shortLived, deleted })
        {
            var response = await Bearer(factory, token).GetAsync(Uri("/api/v1/me"), Cancel);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await response.ProblemTypeAsync()).ShouldBe("unauthorized");
        }
    }

    [Fact]
    public async Task Invalid_token_requests_are_refused()
    {
        await using var factory = Factory();
        var session = await factory.SignedInAsync("ada");

        var past = await session.PostAsJsonAsync("/api/v1/me/tokens", new { name = "ci", expiresAt = factory.Clock.GetUtcNow().AddDays(-1) }, Cancel);
        var unnamed = await session.PostAsJsonAsync("/api/v1/me/tokens", new { name = "" }, Cancel);
        var missing = await session.DeleteAsync(Uri($"/api/v1/me/tokens/{Guid.CreateVersion7()}"), Cancel);

        past.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await past.JsonAsync()).GetProperty("field").GetString().ShouldBe("expiresAt");
        unnamed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tokens_of_users_who_must_change_their_password_are_limited_like_their_sessions()
    {
        await using var factory = Factory();
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var ada = await factory.SignedInAsync("ada", isAdmin: true);
        var (token, _) = await CreateTokenAsync(ada, "ci");
        var adaId = (await (await ada.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/api/v1/users/{adaId}/password-reset", new { password = "a reset password!" }, Cancel)).EnsureSuccessStatusCode();
        var client = Bearer(factory, token);

        (await client.GetAsync(Uri("/api/v1/me"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var blocked = await client.GetAsync(Uri("/api/v1/users"), Cancel);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await blocked.ProblemTypeAsync()).ShouldBe("password-change-required");
    }

    [Fact]
    public async Task Admins_see_and_delete_tokens_of_other_users()
    {
        await using var factory = Factory();
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var ada = await factory.SignedInAsync("ada");
        var (token, id) = await CreateTokenAsync(ada, "leaked");
        var adaId = (await (await ada.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();

        var listed = await (await admin.GetAsync(Uri($"/api/v1/users/{adaId}/tokens"), Cancel)).JsonAsync();
        var userCannot = await ada.GetAsync(Uri($"/api/v1/users/{adaId}/tokens"), Cancel);
        var deleted = await admin.DeleteAsync(Uri($"/api/v1/users/{adaId}/tokens/{id}"), Cancel);

        listed.EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("leaked");
        userCannot.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Bearer(factory, token).GetAsync(Uri("/api/v1/me"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await admin.GetAsync(Uri($"/api/v1/users/{Guid.CreateVersion7()}/tokens"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<(string Token, Guid Id)> CreateTokenAsync(HttpClient session, string name)
    {
        var response = await session.PostAsJsonAsync("/api/v1/me/tokens", new { name }, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.JsonAsync();
        var id = body.GetProperty("details").GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/me/tokens/{id}");
        return (body.GetProperty("token").GetString()!, id);
    }

    private static HttpClient Bearer(AutoCanvasFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);
}

public sealed class TokenApiTestsOnSqlite() : TokenApiTests(DatabaseProvider.Sqlite);

public sealed class TokenApiTestsOnPostgres() : TokenApiTests(DatabaseProvider.Postgres);
