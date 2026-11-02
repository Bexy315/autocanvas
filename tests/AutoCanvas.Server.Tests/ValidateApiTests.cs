using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Server.Tests;

/// <summary>Checking a file without saving it, and the node type catalog editors complete from.</summary>
public abstract class ValidateApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private const string Hello = """
        version: 1
        name: Say hello
        jobs:
          hello:
            steps:
              - run: echo hello
        """;

    [Fact]
    public async Task A_valid_file_is_reported_valid_and_nothing_is_saved()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");

        var result = await ValidateAsync(ada, new { yaml = Hello, folderId = team });

        result.GetProperty("valid").GetBoolean().ShouldBeTrue();
        result.GetProperty("diagnostics").GetArrayLength().ShouldBe(0);
        (await (await ada.GetAsync(Uri($"/api/v1/folders/{team}/automations"), Cancel)).JsonAsync()).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Problems_come_back_with_their_place_in_file_order_with_a_success_status()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");
        const string Broken = """
            version: 1
            jobs:
              build:
                needs: [tset]
                steps:
                  - type: send_mail
            """;

        var result = await ValidateAsync(ada, new { yaml = Broken, folderId = team });

        result.GetProperty("valid").GetBoolean().ShouldBeFalse();
        var diagnostics = result.GetProperty("diagnostics").EnumerateArray().ToList();
        diagnostics.Count.ShouldBeGreaterThanOrEqualTo(2);
        diagnostics.ShouldAllBe(d => string.Equals(d.GetProperty("severity").GetString(), "Error", StringComparison.Ordinal));
        var lines = diagnostics.Select(d => d.GetProperty("line").GetInt32()).ToList();
        lines.ShouldBe(lines.Order());
        diagnostics.ShouldContain(d => d.GetProperty("message").GetString()!.Contains("tset", StringComparison.Ordinal));
        diagnostics.ShouldContain(d => d.GetProperty("message").GetString()!.Contains("send_mail", StringComparison.Ordinal));
        diagnostics[0].GetProperty("line").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task Triggers_are_checked_from_the_folder_as_a_save_would()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var team = await FolderAsync(ada, "Team");
        var build = Id(await (await ada.PostAsJsonAsync($"/api/v1/folders/{team}/automations", new { name = "build", yaml = Hello }, Cancel)).JsonAsync());
        static string Following(string target) => $"""
            version: 1
            triggers:
              automation_completed:
                automation: {target}
            jobs:
              deploy:
                steps:
                  - run: echo deploy
            """;

        var known = await ValidateAsync(ada, new { yaml = Following("build"), folderId = team });
        var unknown = await ValidateAsync(ada, new { yaml = Following("nothing-here"), folderId = team });
        var itself = await ValidateAsync(ada, new { yaml = Following("build"), folderId = team, automationId = build });

        known.GetProperty("valid").GetBoolean().ShouldBeTrue();
        unknown.GetProperty("valid").GetBoolean().ShouldBeFalse();
        unknown.GetProperty("diagnostics")[0].GetProperty("message").GetString()!.ShouldContain("nothing-here");
        itself.GetProperty("valid").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Viewers_may_check_and_others_do_not_learn_the_folder_exists()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var bob = await factory.SignedInAsync("bob");
        var eve = await factory.SignedInAsync("eve");
        var team = await FolderAsync(ada, "Team");
        var other = await FolderAsync(ada, "Other");
        var bobId = (await (await bob.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();
        (await ada.PutAsJsonAsync($"/api/v1/folders/{team}/grants", new { userId = bobId, role = "Viewer" }, Cancel)).EnsureSuccessStatusCode();
        var elsewhere = Id(await (await ada.PostAsJsonAsync($"/api/v1/folders/{other}/automations", new { name = "build", yaml = Hello }, Cancel)).JsonAsync());

        (await ValidateAsync(bob, new { yaml = Hello, folderId = team })).GetProperty("valid").GetBoolean().ShouldBeTrue();
        (await PostAsync(eve, new { yaml = Hello, folderId = team })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(ada, new { yaml = Hello, folderId = team, automationId = elsewhere })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(ada, new { folderId = team })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_node_type_catalog_lists_inputs_and_outputs()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");

        var response = await ada.GetAsync(Uri("/api/v1/node-types"), Cancel);
        var catalog = (await response.JsonAsync()).EnumerateArray().ToList();
        var anonymous = await factory.CreateClient().GetAsync(Uri("/api/v1/node-types"), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        catalog.Select(t => t.GetProperty("name").GetString()).ShouldBe(["docker_build", "http_request"]);
        var http = catalog[1];
        var inputs = http.GetProperty("inputs").EnumerateArray().ToList();
        inputs[0].GetProperty("name").GetString().ShouldBe("url");
        inputs[0].GetProperty("required").GetBoolean().ShouldBeTrue();
        inputs.Single(i => string.Equals(i.GetProperty("name").GetString(), "method", StringComparison.Ordinal)).GetProperty("choices").EnumerateArray().Select(c => c.GetString()).ShouldContain("POST", StringComparer.Ordinal);
        inputs.Single(i => string.Equals(i.GetProperty("name").GetString(), "timeout_seconds", StringComparison.Ordinal)).GetProperty("kind").GetString().ShouldBe("WholeNumber");
        http.GetProperty("outputs").EnumerateArray().Select(o => o.GetProperty("name").GetString()).ShouldBe(["body", "status"]);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> ValidateAsync(HttpClient client, object body)
    {
        var response = await PostAsync(client, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/api/v1/automations/validate", body, Cancel);

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

public sealed class ValidateApiTestsOnSqlite() : ValidateApiTests(DatabaseProvider.Sqlite);

public sealed class ValidateApiTestsOnPostgres() : ValidateApiTests(DatabaseProvider.Postgres);
