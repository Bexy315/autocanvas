using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Server.Tests;

public abstract class AutomationApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private const string Hello = """
        version: 1
        name: Say hello
        jobs:
          hello:
            steps:
              - run: echo hello
        """;

    private const string Goodbye = """
        version: 1
        name: Say goodbye
        jobs:
          bye:
            steps:
              - run: echo bye
        """;

    [Fact]
    public async Task Automations_are_created_saved_as_versions_and_downloaded()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");

        var created = await ada.PostAsJsonAsync($"/api/v1/folders/{team}/automations", new { name = "hello", yaml = Hello, message = "first" }, Cancel);
        var id = Id(await created.JsonAsync());
        var saved = await SendAsync(ada, HttpMethod.Put, $"/api/v1/automations/{id}", "\"1\"", new { yaml = Goodbye, message = "say goodbye" });
        var stale = await SendAsync(ada, HttpMethod.Put, $"/api/v1/automations/{id}", "\"1\"", new { yaml = Hello });
        var file = await ada.GetAsync(Uri($"/api/v1/automations/{id}/yaml"), Cancel);
        var versions = await (await ada.GetAsync(Uri($"/api/v1/automations/{id}/versions"), Cancel)).JsonAsync();
        var first = await (await ada.GetAsync(Uri($"/api/v1/automations/{id}/versions/1"), Cancel)).JsonAsync();
        var list = await (await ada.GetAsync(Uri($"/api/v1/folders/{team}/automations"), Cancel)).JsonAsync();

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.ETag!.Tag.ShouldBe("\"1\"");
        created.Headers.Location!.ToString().ShouldBe($"/api/v1/automations/{id}");
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.Headers.ETag!.Tag.ShouldBe("\"2\"");
        (await saved.JsonAsync()).GetProperty("title").GetString().ShouldBe("Say goodbye");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        file.Content.Headers.ContentType!.MediaType.ShouldBe("text/yaml");
        file.Content.Headers.ContentDisposition!.FileName.ShouldBe("hello.yml");
        file.Headers.ETag!.Tag.ShouldBe("\"2\"");
        (await file.Content.ReadAsStringAsync(Cancel)).ShouldBe(Goodbye);
        versions.GetProperty("items").EnumerateArray().Select(v => v.GetProperty("message").GetString()).ShouldBe(["say goodbye", "first"]);
        versions.GetProperty("items")[0].GetProperty("createdByName").GetString().ShouldBe("ada");
        first.GetProperty("yaml").GetString().ShouldBe(Hello);
        list.GetProperty("items").EnumerateArray().Single().GetProperty("currentVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Invalid_files_are_refused_with_diagnostics_that_point_into_the_file()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");

        var broken = await ada.PostAsJsonAsync($"/api/v1/folders/{team}/automations", new { name = "hello", yaml = Hello + "\n    needs: [nope]\n" }, Cancel);
        var problem = await broken.JsonAsync();

        broken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await broken.ProblemTypeAsync()).ShouldBe("validation");
        problem.GetProperty("field").GetString().ShouldBe("yaml");
        var diagnostic = problem.GetProperty("diagnostics").EnumerateArray().First(d => string.Equals(d.GetProperty("severity").GetString(), "Error", StringComparison.Ordinal));
        diagnostic.GetProperty("code").GetString()!.ShouldStartWith("AC");
        diagnostic.GetProperty("path").GetString()!.ShouldContain("needs");
        diagnostic.GetProperty("line").GetInt32().ShouldBe(7);
        diagnostic.GetProperty("column").GetInt32().ShouldBeGreaterThan(0);
        (await (await ada.GetAsync(Uri($"/api/v1/folders/{team}/automations"), Cancel)).JsonAsync()).GetProperty("items").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Viewers_read_but_only_editors_change()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var bob = await factory.SignedInAsync("bob");
        var eve = await factory.SignedInAsync("eve");
        var team = await FolderAsync(ada, "Team");
        var bobId = (await (await bob.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();
        (await ada.PutAsJsonAsync($"/api/v1/folders/{team}/grants", new { userId = bobId, role = "Viewer" }, Cancel)).EnsureSuccessStatusCode();
        var id = Id(await (await ada.PostAsJsonAsync($"/api/v1/folders/{team}/automations", new { name = "hello", yaml = Hello }, Cancel)).JsonAsync());

        var seen = await (await bob.GetAsync(Uri($"/api/v1/automations/{id}"), Cancel)).JsonAsync();
        var save = await bob.PutAsJsonAsync($"/api/v1/automations/{id}", new { yaml = Goodbye }, Cancel);

        seen.GetProperty("myRole").GetString().ShouldBe("Viewer");
        save.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await bob.DeleteAsync(Uri($"/api/v1/automations/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await eve.GetAsync(Uri($"/api/v1/automations/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await eve.GetAsync(Uri($"/api/v1/automations/{id}/yaml"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateApiClient().GetAsync(Uri($"/api/v1/automations/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Automations_are_renamed_moved_and_deleted_before_their_folder()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var archive = await FolderAsync(ada, "Archive");
        var id = Id(await (await ada.PostAsJsonAsync($"/api/v1/folders/{team}/automations", new { name = "hello", yaml = Hello }, Cancel)).JsonAsync());

        var renamed = await ada.PatchAsJsonAsync($"/api/v1/automations/{id}", new { name = "greet" }, Cancel);
        var badName = await ada.PatchAsJsonAsync($"/api/v1/automations/{id}", new { name = "a/b" }, Cancel);
        var moved = await ada.PostAsJsonAsync($"/api/v1/automations/{id}/move", new { folderId = archive }, Cancel);
        var folderInUse = await ada.DeleteAsync(Uri($"/api/v1/folders/{archive}"), Cancel);
        var deleted = await SendAsync(ada, HttpMethod.Delete, $"/api/v1/automations/{id}", "\"1\"", null);

        (await renamed.JsonAsync()).GetProperty("name").GetString().ShouldBe("greet");
        badName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badName.JsonAsync()).GetProperty("field").GetString().ShouldBe("name");
        (await moved.JsonAsync()).GetProperty("folderId").GetGuid().ShouldBe(archive);
        folderInUse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ada.GetAsync(Uri($"/api/v1/automations/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ada.DeleteAsync(Uri($"/api/v1/folders/{archive}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Missing_and_malformed_fields_are_bad_requests()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var id = Id(await (await ada.PostAsJsonAsync($"/api/v1/folders/{team}/automations", new { name = "hello", yaml = Hello }, Cancel)).JsonAsync());

        (await ada.PostAsJsonAsync($"/api/v1/folders/{team}/automations", new { name = "x" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ada.PostAsJsonAsync($"/api/v1/folders/{team}/automations", new { name = "HELLO", yaml = Hello }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ada.PutAsJsonAsync($"/api/v1/automations/{id}", new { yaml = Hello }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ada.GetAsync(Uri($"/api/v1/automations/{id}/versions?cursor=abc"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ada.GetAsync(Uri($"/api/v1/automations/{id}/versions/9"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> FolderAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/folders", new { name }, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return Id(await response.JsonAsync());
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string ifMatch, object? body)
    {
        var request = new HttpRequestMessage(method, Uri(path)) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return client.SendAsync(request, Cancel);
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);
}

public sealed class AutomationApiTestsOnSqlite() : AutomationApiTests(DatabaseProvider.Sqlite);

public sealed class AutomationApiTestsOnPostgres() : AutomationApiTests(DatabaseProvider.Postgres);
