using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Server.Tests;

public abstract class VariableApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private const string Hello = """
        version: 1
        variables:
          REGION: from-file
        jobs:
          hello:
            steps:
              - run: echo hello
        """;

    [Fact]
    public async Task Values_are_set_by_name_and_secrets_never_come_back()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");

        var created = await ada.PutAsJsonAsync($"/api/v1/folders/{team}/secrets/TOKEN", new { value = "s3cr3t-value" }, Cancel);
        var replaced = await ada.PutAsJsonAsync($"/api/v1/folders/{team}/secrets/token", new { value = "n3w-s3cr3t" }, Cancel);
        var variable = await ada.PutAsJsonAsync($"/api/v1/folders/{team}/variables/REGION", new { value = "eu" }, Cancel);
        var secrets = await ada.GetStringAsync(Uri($"/api/v1/folders/{team}/secrets"), Cancel);
        var variables = await (await ada.GetAsync(Uri($"/api/v1/folders/{team}/variables"), Cancel)).JsonAsync();

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location!.ToString().ShouldBe($"/api/v1/folders/{team}/secrets/TOKEN");
        (await created.Content.ReadAsStringAsync(Cancel)).ShouldNotContain("s3cr3t");
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replaced.JsonAsync()).GetProperty("name").GetString().ShouldBe("token");
        variable.StatusCode.ShouldBe(HttpStatusCode.Created);
        secrets.ShouldContain("\"name\":\"token\"");
        secrets.ShouldNotContain("s3cr3t");
        secrets.ShouldNotContain("value");
        variables.EnumerateArray().Single().GetProperty("value").GetString().ShouldBe("eu");
        (await ada.DeleteAsync(Uri($"/api/v1/folders/{team}/secrets/TOKEN"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ada.DeleteAsync(Uri($"/api/v1/folders/{team}/variables/REGION"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ada.DeleteAsync(Uri($"/api/v1/folders/{team}/variables/REGION"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_instance_scope_is_read_by_everyone_and_changed_by_admins()
    {
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var ada = await factory.SignedInAsync("ada");

        (await root.PutAsJsonAsync("/api/v1/variables/DOMAIN", new { value = "example.org" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await root.PutAsJsonAsync("/api/v1/secrets/SMTP", new { value = "mail-pass" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await (await ada.GetAsync(Uri("/api/v1/variables"), Cancel)).JsonAsync()).EnumerateArray().Single().GetProperty("value").GetString().ShouldBe("example.org");
        (await (await ada.GetAsync(Uri("/api/v1/secrets"), Cancel)).JsonAsync()).EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("SMTP");
        var forbidden = await ada.PutAsJsonAsync("/api/v1/variables/DOMAIN", new { value = "evil.org" }, Cancel);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ada.DeleteAsync(Uri("/api/v1/secrets/SMTP"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await root.DeleteAsync(Uri("/api/v1/secrets/SMTP"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await root.DeleteAsync(Uri("/api/v1/variables/DOMAIN"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Explaining_an_automation_shows_where_values_come_from()
    {
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var automation = Id(await (await ada.PostAsJsonAsync($"/api/v1/folders/{team}/automations", new { name = "hello", yaml = Hello }, Cancel)).JsonAsync());
        await root.PutAsJsonAsync("/api/v1/variables/REGION", new { value = "instance" }, Cancel);
        await ada.PutAsJsonAsync($"/api/v1/folders/{team}/variables/REGION", new { value = "team" }, Cancel);
        await ada.PutAsJsonAsync($"/api/v1/folders/{team}/secrets/TOKEN", new { value = "team-token" }, Cancel);
        (await ada.PutAsJsonAsync($"/api/v1/automations/{automation}/secrets/TOKEN", new { value = "own-token" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await ada.GetAsync(Uri($"/api/v1/automations/{automation}/configuration"), Cancel);
        var view = await response.JsonAsync();
        var folderView = await (await ada.GetAsync(Uri($"/api/v1/folders/{team}/configuration"), Cancel)).JsonAsync();

        var region = view.GetProperty("variables").EnumerateArray().Single();
        region.GetProperty("definitions").EnumerateArray().Select(d => d.GetProperty("kind").GetString()).ShouldBe(["Instance", "Folder", "Automation"]);
        region.GetProperty("winner").GetProperty("value").GetString().ShouldBe("from-file");
        var token = view.GetProperty("secrets").EnumerateArray().Single();
        token.GetProperty("winner").GetProperty("kind").GetString().ShouldBe("Automation");
        (await response.Content.ReadAsStringAsync(Cancel)).ShouldNotContain("team-token");
        (await response.Content.ReadAsStringAsync(Cancel)).ShouldNotContain("own-token");
        folderView.GetProperty("variables").EnumerateArray().Single().GetProperty("winner").GetProperty("folderName").GetString().ShouldBe("Team");
    }

    [Fact]
    public async Task Viewers_see_names_and_outsiders_nothing()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var vic = await factory.SignedInAsync("vic");
        var eve = await factory.SignedInAsync("eve");
        var team = await FolderAsync(ada, "Team");
        var vicId = (await (await vic.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();
        (await ada.PutAsJsonAsync($"/api/v1/folders/{team}/grants", new { userId = vicId, role = "Viewer" }, Cancel)).EnsureSuccessStatusCode();
        await ada.PutAsJsonAsync($"/api/v1/folders/{team}/variables/REGION", new { value = "eu" }, Cancel);

        var seen = (await (await vic.GetAsync(Uri($"/api/v1/folders/{team}/variables"), Cancel)).JsonAsync()).EnumerateArray().Single();

        seen.GetProperty("name").GetString().ShouldBe("REGION");
        seen.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        (await vic.PutAsJsonAsync($"/api/v1/folders/{team}/variables/REGION", new { value = "us" }, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await eve.GetAsync(Uri($"/api/v1/folders/{team}/secrets"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await eve.GetAsync(Uri($"/api/v1/folders/{team}/configuration"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await eve.GetAsync(Uri($"/api/v1/automations/{Guid.CreateVersion7()}/configuration"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateApiClient().GetAsync(Uri("/api/v1/variables"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Invalid_names_and_values_are_explained()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");

        var badName = await ada.PutAsJsonAsync($"/api/v1/folders/{team}/variables/1BAD", new { value = "x" }, Cancel);
        var emptySecret = await ada.PutAsJsonAsync($"/api/v1/folders/{team}/secrets/TOKEN", new { value = string.Empty }, Cancel);
        var noValue = await ada.PutAsJsonAsync($"/api/v1/folders/{team}/secrets/TOKEN", new { }, Cancel);

        badName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await badName.JsonAsync()).GetProperty("field").GetString().ShouldBe("name");
        emptySecret.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await emptySecret.JsonAsync()).GetProperty("field").GetString().ShouldBe("value");
        noValue.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> FolderAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/folders", new { name }, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return Id(await response.JsonAsync());
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);
}

public sealed class VariableApiTestsOnSqlite() : VariableApiTests(DatabaseProvider.Sqlite);

public sealed class VariableApiTestsOnPostgres() : VariableApiTests(DatabaseProvider.Postgres);
