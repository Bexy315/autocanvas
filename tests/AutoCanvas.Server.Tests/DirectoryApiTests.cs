using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Server.Tests;

public abstract class DirectoryApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task Everyone_signed_in_finds_people_and_groups_by_name_without_their_addresses()
    {
        await using var factory = new AutoCanvasFactory().WithDatabase(Database);
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var vic = await factory.SignedInAsync("vic");
        foreach (var (name, display) in new[] { ("ada", "Ada Lovelace"), ("grace", "Grace Hopper"), ("adam", "Adam Smith"), ("old", "Ada Old") })
        {
            (await root.PostAsJsonAsync("/api/v1/users", new { userName = name, password = ApiClient.Password, displayName = display, email = $"{name}@example.org" }, Cancel)).EnsureSuccessStatusCode();
        }

        var oldId = await IdAsync(root, "/api/v1/users?limit=100", "old");
        (await root.PatchAsJsonAsync($"/api/v1/users/{oldId}", new { isDisabled = true }, Cancel)).EnsureSuccessStatusCode();
        (await root.PostAsJsonAsync("/api/v1/groups", new { name = "readers" }, Cancel)).EnsureSuccessStatusCode();
        (await root.PostAsJsonAsync("/api/v1/groups", new { name = "data team" }, Cancel)).EnsureSuccessStatusCode();

        var found = await (await vic.GetAsync(new Uri("/api/v1/directory?query=ADA", UriKind.Relative), Cancel)).JsonAsync();
        var groups = await (await vic.GetAsync(new Uri("/api/v1/directory?query=rea", UriKind.Relative), Cancel)).JsonAsync();
        var hopper = await (await vic.GetAsync(new Uri("/api/v1/directory?query=hop", UriKind.Relative), Cancel)).JsonAsync();

        // Names that start with the query first; the disabled account not at all; no e-mail addresses.
        Names(found, "users", "userName").ShouldBe(["ada", "adam"]);
        found.GetProperty("users")[0].TryGetProperty("email", out _).ShouldBeFalse();
        Names(groups, "groups", "name").ShouldBe(["readers"]);
        Names(hopper, "users", "userName").ShouldBe(["grace"]);
        (await vic.GetAsync(new Uri("/api/v1/directory?query=a", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.CreateApiClient().GetAsync(new Uri("/api/v1/directory?query=ada", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static string[] Names(JsonElement view, string list, string property) =>
        [.. view.GetProperty(list).EnumerateArray().Select(item => item.GetProperty(property).GetString()!)];

    private static async Task<Guid> IdAsync(HttpClient client, string path, string userName)
    {
        var page = await (await client.GetAsync(new Uri(path, UriKind.Relative), Cancel)).JsonAsync();
        return page.GetProperty("items").EnumerateArray().Single(u => string.Equals(u.GetProperty("userName").GetString(), userName, StringComparison.Ordinal)).GetProperty("id").GetGuid();
    }
}

public sealed class DirectoryApiTestsOnSqlite() : DirectoryApiTests(DatabaseProvider.Sqlite);

public sealed class DirectoryApiTestsOnPostgres() : DirectoryApiTests(DatabaseProvider.Postgres);
