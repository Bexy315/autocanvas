using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Server.Tests;

public abstract class FolderApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task Teams_share_folders_through_groups_and_restrict_parts_of_them()
    {
        await using var factory = Factory();
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var ada = await factory.SignedInAsync("ada");
        var bob = await factory.SignedInAsync("bob");
        var bobId = await UserIdAsync(bob);
        var ops = await (await admin.PostAsJsonAsync("/api/v1/groups", new { name = "ops" }, Cancel)).JsonAsync();
        (await admin.PutAsync(Uri($"/api/v1/groups/{Id(ops)}/members/{bobId}"), null, Cancel)).EnsureSuccessStatusCode();

        var team = await CreateAsync(ada, "Team");
        var prod = await CreateAsync(ada, "Prod", Id(team));
        (await ada.PutAsJsonAsync($"/api/v1/folders/{Id(team)}/grants", new { groupId = Id(ops), role = "Viewer" }, Cancel)).EnsureSuccessStatusCode();

        var entry = (await (await bob.GetAsync(Uri("/api/v1/folders"), Cancel)).JsonAsync()).EnumerateArray().Single();
        entry.GetProperty("name").GetString().ShouldBe("Team");
        entry.GetProperty("myRole").GetString().ShouldBe("Viewer");
        var rename = await bob.PatchAsJsonAsync($"/api/v1/folders/{Id(team)}", new { name = "Mine" }, Cancel);
        rename.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await rename.ProblemTypeAsync()).ShouldBe("forbidden");

        (await ada.PutAsJsonAsync($"/api/v1/folders/{Id(prod)}/restriction", new { isRestricted = true }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bob.GetAsync(Uri($"/api/v1/folders/{Id(prod)}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var children = await (await bob.GetAsync(Uri($"/api/v1/folders/{Id(team)}/children"), Cancel)).JsonAsync();
        children.GetProperty("items").EnumerateArray().ShouldBeEmpty();

        (await ada.PutAsJsonAsync($"/api/v1/folders/{Id(prod)}/grants", new { userId = bobId, role = "Editor" }, Cancel)).EnsureSuccessStatusCode();
        var seen = await (await bob.GetAsync(Uri($"/api/v1/folders/{Id(prod)}"), Cancel)).JsonAsync();
        seen.GetProperty("myRole").GetString().ShouldBe("Editor");
        seen.GetProperty("path")[0].GetProperty("name").GetString().ShouldBe("Team");

        var grants = await (await ada.GetAsync(Uri($"/api/v1/folders/{Id(prod)}/grants"), Cancel)).JsonAsync();
        grants.EnumerateArray().Select(g => g.GetProperty("subjectName").GetString()).ShouldBe(["ada", "bob"]);
        (await bob.GetAsync(Uri($"/api/v1/folders/{Id(prod)}/grants"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Changes_check_the_etag_when_one_is_sent()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var created = await ada.PostAsJsonAsync("/api/v1/folders", new { name = "Team" }, Cancel);
        var id = Id(await created.JsonAsync());
        created.Headers.ETag!.Tag.ShouldBe("\"1\"");
        created.Headers.Location!.ToString().ShouldBe($"/api/v1/folders/{id}");

        var renamed = await PatchAsync(ada, id, "\"1\"", new { name = "Squad" });
        var stale = await PatchAsync(ada, id, "\"1\"", new { name = "Again" });
        var weak = await PatchAsync(ada, id, "W/\"2\"", new { description = "ok" });
        var garbage = await PatchAsync(ada, id, "not-an-etag", new { name = "x" });
        var unconditional = await ada.PatchAsJsonAsync($"/api/v1/folders/{id}", new { name = "Final" }, Cancel);

        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        renamed.Headers.ETag!.Tag.ShouldBe("\"2\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await stale.ProblemTypeAsync()).ShouldBe("precondition-failed");
        weak.StatusCode.ShouldBe(HttpStatusCode.OK);
        garbage.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        unconditional.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ada.GetAsync(Uri($"/api/v1/folders/{id}"), Cancel)).Headers.ETag!.Tag.ShouldBe("\"4\"");
    }

    [Fact]
    public async Task Folders_move_and_are_deleted_once_empty()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await CreateAsync(ada, "Team");
        var archive = await CreateAsync(ada, "Archive");
        var prod = await CreateAsync(ada, "Prod", Id(team));

        var moved = await ada.PostAsJsonAsync($"/api/v1/folders/{Id(prod)}/move", new { parentId = Id(archive) }, Cancel);
        var intoItself = await ada.PostAsJsonAsync($"/api/v1/folders/{Id(archive)}/move", new { parentId = Id(prod) }, Cancel);
        var withoutParent = await ada.PostAsJsonAsync($"/api/v1/folders/{Id(prod)}/move", new { }, Cancel);
        var toTop = await ada.PostAsJsonAsync($"/api/v1/folders/{Id(prod)}/move", new { parentId = (Guid?)null }, Cancel);
        var notEmpty = await ada.DeleteAsync(Uri($"/api/v1/folders/{Id(team)}"), Cancel);

        (await moved.JsonAsync()).GetProperty("parentId").GetGuid().ShouldBe(Id(archive));
        intoItself.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        withoutParent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await toTop.JsonAsync()).GetProperty("parentId").ValueKind.ShouldBe(JsonValueKind.Null);
        notEmpty.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ada.DeleteAsync(Uri($"/api/v1/folders/{Id(archive)}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await CreateAsync(ada, "Child", Id(prod));
        var refused = await ada.DeleteAsync(Uri($"/api/v1/folders/{Id(prod)}"), Cancel);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_folder_requests_are_explained()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await CreateAsync(ada, "Team");

        var slash = await ada.PostAsJsonAsync("/api/v1/folders", new { name = "a/b" }, Cancel);
        var taken = await ada.PostAsJsonAsync("/api/v1/folders", new { name = "TEAM" }, Cancel);
        var badRole = await ada.PutAsJsonAsync($"/api/v1/folders/{Id(team)}/grants", new { role = "Boss", userId = await UserIdAsync(ada) }, Cancel);
        var noSubject = await ada.PutAsJsonAsync($"/api/v1/folders/{Id(team)}/grants", new { role = "Viewer" }, Cancel);
        var missingGrant = await ada.DeleteAsync(Uri($"/api/v1/folders/{Id(team)}/grants/{Guid.CreateVersion7()}"), Cancel);
        var badPage = await ada.GetAsync(Uri($"/api/v1/folders/{Id(team)}/children?limit=500"), Cancel);
        var anonymous = await factory.CreateApiClient().GetAsync(Uri("/api/v1/folders"), Cancel);

        slash.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await slash.JsonAsync()).GetProperty("field").GetString().ShouldBe("name");
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        badRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noSubject.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        missingGrant.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        badPage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Owners_remove_grants_and_admins_see_everything()
    {
        await using var factory = Factory();
        var admin = await factory.SignedInAsync("root", isAdmin: true);
        var ada = await factory.SignedInAsync("ada");
        var bob = await factory.SignedInAsync("bob");
        var team = await CreateAsync(ada, "Team");
        var grant = await (await ada.PutAsJsonAsync($"/api/v1/folders/{Id(team)}/grants", new { userId = await UserIdAsync(bob), role = "Viewer" }, Cancel)).JsonAsync();

        (await bob.GetAsync(Uri($"/api/v1/folders/{Id(team)}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ada.DeleteAsync(Uri($"/api/v1/folders/{Id(team)}/grants/{Id(grant)}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await bob.GetAsync(Uri($"/api/v1/folders/{Id(team)}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await ada.PutAsJsonAsync($"/api/v1/folders/{Id(team)}/restriction", new { isRestricted = true }, Cancel)).EnsureSuccessStatusCode();
        var roots = await (await admin.GetAsync(Uri("/api/v1/folders"), Cancel)).JsonAsync();
        roots.EnumerateArray().Single().GetProperty("myRole").GetString().ShouldBe("Owner");
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string name, Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/folders", new { name, parentId }, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.JsonAsync();
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, string ifMatch, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, Uri($"/api/v1/folders/{id}")) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return client.SendAsync(request, Cancel);
    }

    private static async Task<Guid> UserIdAsync(HttpClient client) =>
        (await (await client.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);
}

public sealed class FolderApiTestsOnSqlite() : FolderApiTests(DatabaseProvider.Sqlite);

public sealed class FolderApiTestsOnPostgres() : FolderApiTests(DatabaseProvider.Postgres);
