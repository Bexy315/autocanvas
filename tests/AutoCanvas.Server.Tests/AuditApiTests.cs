using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Tests;

public abstract class AuditApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    [Fact]
    public async Task Admins_read_the_log_with_who_did_what()
    {
        await using var factory = Factory();
        var root = await factory.SignedInAsync("root", isAdmin: true);
        var ada = await factory.SignedInAsync("ada");
        var adaId = (await (await ada.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();
        var team = Id(await (await ada.PostAsJsonAsync("/api/v1/folders", new { name = "Team" }, Cancel)).JsonAsync());

        var byAda = await (await root.GetAsync(Uri($"/api/v1/audit?actorId={adaId}&action=folder.*"), Cancel)).JsonAsync();
        var created = byAda.GetProperty("items").EnumerateArray().Single(e => string.Equals(e.GetProperty("action").GetString(), "folder.created", StringComparison.Ordinal));

        created.GetProperty("actorKind").GetString().ShouldBe("User");
        created.GetProperty("actorName").GetString().ShouldBe("ada");
        created.GetProperty("folderId").GetGuid().ShouldBe(team);
        created.GetProperty("details").GetProperty("name").GetString().ShouldBe("Team");
        created.GetProperty("userAgent").GetString().ShouldBe("autocanvas-tests/1.0");
        (await ada.GetAsync(Uri("/api/v1/audit"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await root.GetAsync(Uri("/api/v1/audit?from=yesterday"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await root.GetAsync(Uri("/api/v1/audit?cursor=nope"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.CreateApiClient().GetAsync(Uri("/api/v1/audit"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Owners_read_the_log_of_their_folders()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var bob = await factory.SignedInAsync("bob");
        var team = Id(await (await ada.PostAsJsonAsync("/api/v1/folders", new { name = "Team" }, Cancel)).JsonAsync());
        var bobId = (await (await bob.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();
        (await ada.PutAsJsonAsync($"/api/v1/folders/{team}/grants", new { userId = bobId, role = "Editor" }, Cancel)).EnsureSuccessStatusCode();

        var log = await (await ada.GetAsync(Uri($"/api/v1/folders/{team}/audit?limit=1"), Cancel)).JsonAsync();
        var next = log.GetProperty("next").GetString();
        var rest = await (await ada.GetAsync(Uri($"/api/v1/folders/{team}/audit?cursor={next}"), Cancel)).JsonAsync();

        // The test clock stands still, so the order within one instant is arbitrary: check the pages together.
        var all = log.GetProperty("items").EnumerateArray().Concat(rest.GetProperty("items").EnumerateArray()).ToList();
        all.Select(e => e.GetProperty("id").GetGuid()).ShouldBeUnique();
        all.Select(e => e.GetProperty("action").GetString()).ShouldBe(["folder.created", "folder.grant_set", "folder.grant_set"], ignoreOrder: true);
        (await bob.GetAsync(Uri($"/api/v1/folders/{team}/audit"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Retention_must_not_be_negative()
    {
        (await ServerStart.FailsAsync<OptionsValidationException>(Database, ("Audit:RetentionDays", "-1"))).ShouldContain("Audit:RetentionDays");
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);
}

public sealed class AuditApiTestsOnSqlite() : AuditApiTests(DatabaseProvider.Sqlite);

public sealed class AuditApiTestsOnPostgres() : AuditApiTests(DatabaseProvider.Postgres);
