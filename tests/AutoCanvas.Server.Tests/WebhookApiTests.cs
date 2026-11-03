using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoCanvas.Persistence;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Server.Tests;

public abstract class WebhookApiTests(DatabaseProvider provider) : DatabaseTest(provider)
{
    private const string Hooked = """
        version: 1
        triggers:
          webhook: true
        jobs:
          build:
            steps:
              - run: echo ${{ trigger.body.ref }} ${{ trigger.headers.X-Delivery }}
        """;

    [Fact]
    public async Task The_whole_address_follows_the_public_address()
    {
        await using var factory = Factory().With("Server:PublicUrl", "http://autocanvas.example.org");
        var ada = await factory.SignedInAsync("ada");
        var automation = await AutomationAsync(ada, Hooked);

        var hook = await (await ada.PostAsJsonAsync($"/api/v1/automations/{automation}/webhook", new { }, Cancel)).JsonAsync();

        hook.GetProperty("url").GetString().ShouldBe($"http://autocanvas.example.org{hook.GetProperty("path").GetString()}");
    }

    [Fact]
    public async Task Another_system_starts_runs_through_the_address_without_signing_in()
    {
        await using var factory = Factory();
        var ada = await factory.SignedInAsync("ada");
        var automation = await AutomationAsync(ada, Hooked);
        var created = await ada.PostAsJsonAsync($"/api/v1/automations/{automation}/webhook", new { requireSignature = true }, Cancel);
        var hook = await created.JsonAsync();
        var path = hook.GetProperty("path").GetString()!;
        var secret = hook.GetProperty("signingSecret").GetString()!;
        hook.GetProperty("url").ValueKind.ShouldBe(JsonValueKind.Null);
        using var outsider = factory.CreateClient();
        const string Body = """{ "ref": "main" }""";

        var signed = await outsider.SendAsync(Signed(path, Body, secret), Cancel);
        var unsigned = await outsider.PostAsync(new Uri(path, UriKind.Relative), new StringContent(Body, Encoding.UTF8, "application/json"), Cancel);
        var view = await (await ada.GetAsync(Uri($"/api/v1/automations/{automation}/webhook"), Cancel)).JsonAsync();
        var triggers = await (await ada.GetAsync(Uri($"/api/v1/automations/{automation}/triggers"), Cancel)).JsonAsync();

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        signed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var run = await signed.JsonAsync();
        signed.Headers.Location!.ToString().ShouldBe($"/api/v1/runs/{run.GetProperty("runId").GetGuid()}");
        var read = await (await ada.GetAsync(Uri($"/api/v1/runs/{run.GetProperty("runId").GetGuid()}"), Cancel)).JsonAsync();
        read.GetProperty("trigger").GetString().ShouldBe("Webhook");
        unsigned.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await unsigned.ProblemTypeAsync()).ShouldBe("unauthorized");
        view.GetProperty("requiresSignature").GetBoolean().ShouldBeTrue();
        view.TryGetProperty("token", out _).ShouldBeFalse();
        triggers.GetProperty("webhook").GetBoolean().ShouldBeTrue();
        (await ada.DeleteAsync(Uri($"/api/v1/automations/{automation}/webhook"), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await outsider.SendAsync(Signed(path, Body, secret), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Calls_that_are_too_large_or_too_many_are_refused()
    {
        await using var factory = Factory().With("Webhooks:PerMinute", "2");
        var ada = await factory.SignedInAsync("ada");
        var automation = await AutomationAsync(ada, Hooked);
        var path = (await (await ada.PostAsJsonAsync($"/api/v1/automations/{automation}/webhook", new { }, Cancel)).JsonAsync()).GetProperty("path").GetString()!;
        using var outsider = factory.CreateClient();

        var large = await outsider.PostAsync(new Uri(path, UriKind.Relative), new ByteArrayContent(new byte[(1024 * 1024) + 1]), Cancel);
        var second = await outsider.PostAsync(new Uri(path, UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Cancel);
        var third = await outsider.PostAsync(new Uri(path, UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Cancel);

        large.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await large.ProblemTypeAsync()).ShouldBe("too-large");
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await third.JsonAsync()).GetProperty("detail").GetString()!.ShouldContain("this webhook");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("20000")]
    public async Task The_webhook_limit_must_be_sensible(string perMinute) =>
        (await ServerStart.FailsAsync<Microsoft.Extensions.Options.OptionsValidationException>(Database, ("Webhooks:PerMinute", perMinute))).ShouldContain("Webhooks:PerMinute");

    private static HttpRequestMessage Signed(string path, string body, string secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-AutoCanvas-Signature", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))));
        request.Headers.Add("X-Delivery", "42");
        return request;
    }

    private static async Task<Guid> AutomationAsync(HttpClient client, string yaml)
    {
        var folder = Id(await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Team" }, Cancel)).JsonAsync());
        var response = await client.PostAsJsonAsync($"/api/v1/folders/{folder}/automations", new { name = "hooked", yaml }, Cancel);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return Id(await response.JsonAsync());
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static Uri Uri(string path) => new(path, UriKind.Relative);

    private AutoCanvasFactory Factory() => new AutoCanvasFactory().WithDatabase(Database);
}

public sealed class WebhookApiTestsOnSqlite() : WebhookApiTests(DatabaseProvider.Sqlite);

public sealed class WebhookApiTestsOnPostgres() : WebhookApiTests(DatabaseProvider.Postgres);
