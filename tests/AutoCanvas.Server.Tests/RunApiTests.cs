using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Server.Tests;

public abstract class RunApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private const string Build = """
        version: 1
        name: Build
        triggers:
          manual: true
        concurrency: single
        jobs:
          compile:
            steps:
              - run: echo compile
          test:
            needs: [compile]
            steps:
              - run: echo test
        """;

    [Fact]
    public async Task Runs_are_started_read_listed_and_cancelled()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var automation = await AutomationAsync(ada, Build);

        var started = await ada.PostAsync(Uri($"/api/v1/automations/{automation}/runs"), null, Cancel);
        var run = await started.JsonAsync();
        var id = Id(run);
        var busy = await ada.PostAsync(Uri($"/api/v1/automations/{automation}/runs"), null, Cancel);
        var read = await (await ada.GetAsync(Uri($"/api/v1/runs/{id}"), Cancel)).JsonAsync();
        var list = await (await ada.GetAsync(Uri($"/api/v1/automations/{automation}/runs"), Cancel)).JsonAsync();
        var cancelled = await ada.PostAsync(Uri($"/api/v1/runs/{id}/cancel"), null, Cancel);

        started.StatusCode.ShouldBe(HttpStatusCode.Created);
        started.Headers.Location!.ToString().ShouldBe($"/api/v1/runs/{id}");
        run.GetProperty("number").GetInt32().ShouldBe(1);
        run.GetProperty("state").GetString().ShouldBe("Running");
        run.GetProperty("trigger").GetString().ShouldBe("Manual");
        run.GetProperty("jobs").EnumerateArray().Select(j => (j.GetProperty("key").GetString(), j.GetProperty("status").GetString()))
            .ShouldBe([("compile", "Running"), ("test", "Pending")]);
        busy.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        read.GetProperty("automationName").GetString().ShouldBe("build");
        list.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(id);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await cancelled.JsonAsync();
        after.GetProperty("state").GetString().ShouldBe("Cancelled");
        after.GetProperty("cancelledByName").GetString().ShouldBe("ada");
    }

    [Fact]
    public async Task Viewers_read_runs_but_do_not_start_or_cancel_them()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var vic = await factory.SignedInAsync("vic");
        var eve = await factory.SignedInAsync("eve");
        var automation = await AutomationAsync(ada, Build);
        var folder = (await (await ada.GetAsync(Uri($"/api/v1/automations/{automation}"), Cancel)).JsonAsync()).GetProperty("folderId").GetGuid();
        var vicId = (await (await vic.GetAsync(Uri("/api/v1/me"), Cancel)).JsonAsync()).GetProperty("user").GetProperty("id").GetGuid();
        (await ada.PutAsJsonAsync($"/api/v1/folders/{folder}/grants", new { userId = vicId, role = "Viewer" }, Cancel)).EnsureSuccessStatusCode();
        var id = Id(await (await ada.PostAsync(Uri($"/api/v1/automations/{automation}/runs"), null, Cancel)).JsonAsync());

        (await vic.GetAsync(Uri($"/api/v1/runs/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await vic.PostAsync(Uri($"/api/v1/runs/{id}/cancel"), null, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vic.PostAsync(Uri($"/api/v1/automations/{automation}/runs"), null, Cancel)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await eve.GetAsync(Uri($"/api/v1/runs/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await eve.GetAsync(Uri($"/api/v1/automations/{automation}/runs"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.CreateApiClient().GetAsync(Uri($"/api/v1/runs/{id}"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Automations_that_do_not_allow_it_are_not_started_by_hand()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var automation = await AutomationAsync(ada, Build.Replace("  manual: true", "  webhook: true", StringComparison.Ordinal));

        var refused = await ada.PostAsync(Uri($"/api/v1/automations/{automation}/runs"), null, Cancel);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("manual: true");
        (await ada.GetAsync(Uri($"/api/v1/automations/{automation}/runs?cursor=x"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> AutomationAsync(HttpClient client, string yaml)
    {
        var folder = Id(await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Team" }, Cancel)).JsonAsync());
        var response = await client.PostAsJsonAsync($"/api/v1/folders/{folder}/automations", new { name = "build", yaml }, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return Id(await response.JsonAsync());
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);
}

public sealed class RunApiTestsOnSqlite() : RunApiTests(DatabaseProvider.Sqlite);

public sealed class RunApiTestsOnPostgres() : RunApiTests(DatabaseProvider.Postgres);
