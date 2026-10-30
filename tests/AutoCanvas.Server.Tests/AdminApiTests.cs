using System.Net;
using System.Net.Http.Json;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Tests;

public abstract class AdminApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task Admins_create_read_change_and_delete_users()
    {
        await using var factory = Factory();
        var admin = await factory.SignedInAsync("root", isAdmin: true);

        var created = await admin.PostAsJsonAsync("/api/v1/users", new { userName = "ada", password = ApiClient.Password, displayName = "Ada", email = "ada@example.org" }, Cancel);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var user = await created.JsonAsync();
        var id = user.GetProperty("id").GetGuid();
        created.Headers.Location!.ToString().ShouldBe($"/api/v1/users/{id}");
        user.GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();
        user.TryGetProperty("passwordHash", out _).ShouldBeFalse();

        (await (await admin.GetAsync(Uri($"/api/v1/users/{id}"), Cancel)).JsonAsync()).GetProperty("email").GetString().ShouldBe("ada@example.org");

        var patched = await (await admin.PatchAsJsonAsync($"/api/v1/users/{id}", new { displayName = "Ada Lovelace", email = "" }, Cancel)).JsonAsync();
        patched.GetProperty("displayName").GetString().ShouldBe("Ada Lovelace");
        patched.GetProperty("email").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);

        (await admin.PostAsJsonAsync($"/api/v1/users/{id}/password-reset", new { password = "another long password" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.CreateApiClient().SignInAsync("ada", "another long password")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.DeleteAsync(Uri($"/api/v1/users/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync(Uri($"/api/v1/users/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Refusals_come_as_problems_with_the_field_at_fault()
    {
        await using var factory = Factory();
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var me = (await (await admin.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();

        var invalid = await admin.PostAsJsonAsync("/api/v1/users", new { userName = "ada lovelace", password = ApiClient.Password }, Cancel);
        var taken = await admin.PostAsJsonAsync("/api/v1/users", new { userName = "ROOT", password = ApiClient.Password }, Cancel);
        var lastAdmin = await admin.PatchAsJsonAsync($"/api/v1/users/{me}", new { isAdmin = false }, Cancel);
        var missing = await admin.GetAsync(Uri($"/api/v1/users/{Guid.CreateVersion7()}"), Cancel);
        var badPage = await admin.GetAsync(Uri("/api/v1/users?limit=0"), Cancel);

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.ProblemTypeAsync()).ShouldBe("validation");
        (await invalid.JsonAsync()).GetProperty("field").GetString().ShouldBe("userName");
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await taken.ProblemTypeAsync()).ShouldBe("conflict");
        lastAdmin.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await lastAdmin.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("last enabled admin");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.ProblemTypeAsync()).ShouldBe("not-found");
        badPage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task User_lists_are_paged()
    {
        await using var factory = Factory();
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        await factory.CreateUserAsync("ada");
        await factory.CreateUserAsync("bob");

        var first = await (await admin.GetAsync(Uri("/api/v1/users?limit=2"), Cancel)).JsonAsync();
        var next = first.GetProperty("next").GetString();
        var second = await (await admin.GetAsync(Uri($"/api/v1/users?limit=2&cursor={next}"), Cancel)).JsonAsync();

        first.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ShouldBe(["ada", "bob"]);
        second.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("userName").GetString()).ShouldBe(["root"]);
        second.GetProperty("next").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Only_admins_manage_users_and_groups()
    {
        await using var factory = Factory();
        var user = await factory.SignedInAsync("ada");

        var users = await user.GetAsync(Uri("/api/v1/users"), Cancel);
        var groups = await user.PostAsJsonAsync("/api/v1/groups", new { name = "ops" }, Cancel);

        users.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await users.ProblemTypeAsync()).ShouldBe("forbidden");
        groups.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admins_manage_groups_and_members()
    {
        await using var factory = Factory();
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var ada = await factory.CreateUserAsync("ada");

        var created = await admin.PostAsJsonAsync("/api/v1/groups", new { name = "Operations", description = "On call" }, Cancel);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.JsonAsync()).GetProperty("id").GetGuid();

        (await admin.PutAsync(Uri($"/api/v1/groups/{id}/members/{ada.Id}"), null, Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var details = await (await admin.GetAsync(Uri($"/api/v1/groups/{id}"), Cancel)).JsonAsync();
        details.GetProperty("members").EnumerateArray().Single().GetProperty("userName").GetString().ShouldBe("ada");

        var list = await (await admin.GetAsync(Uri("/api/v1/groups"), Cancel)).JsonAsync();
        list.GetProperty("items").EnumerateArray().Single().GetProperty("memberCount").GetInt32().ShouldBe(1);

        var adaClient = factory.CreateApiClient();
        (await adaClient.SignInAsync("ada")).EnsureSuccessStatusCode();
        var me = await (await adaClient.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync();
        me.GetProperty("groups").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Operations");

        (await (await admin.PatchAsJsonAsync($"/api/v1/groups/{id}", new { name = "Ops" }, Cancel)).JsonAsync()).GetProperty("name").GetString().ShouldBe("Ops");
        (await admin.DeleteAsync(Uri($"/api/v1/groups/{id}/members/{ada.Id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PutAsync(Uri($"/api/v1/groups/{id}/members/{Guid.CreateVersion7()}"), null, Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.DeleteAsync(Uri($"/api/v1/groups/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync(Uri($"/api/v1/groups/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Changes_over_http_are_recorded_with_the_admin_and_client()
    {
        await using var factory = Factory();
        var admin = await factory.SignedInAsync("root", isAdmin: true);

        (await admin.PostAsJsonAsync("/api/v1/groups", new { name = "ops" }, Cancel)).EnsureSuccessStatusCode();

        var context = Database.CreateContext();
        await using (context)
        {
            var created = await context.AuditEvents.SingleAsync(e => e.Action == "group.created", Cancel);
            created.ActorKind.ShouldBe(AuditActorKind.User);
            created.ActorName.ShouldBe("root");
            created.UserAgent.ShouldBe("autocanvas-tests/1.0");
        }
    }

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);
}

public sealed class AdminApiTestsOnSqlite() : AdminApiTests(DatabaseProvider.Sqlite);

public sealed class AdminApiTestsOnPostgres() : AdminApiTests(DatabaseProvider.Postgres);
