using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AutoCanvas.Server.Tests;

public sealed class HttpTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Info_names_the_product_and_needs_no_login()
    {
        await using var factory = new AutoCanvasFactory();

        var response = await factory.CreateClient().GetAsync(new Uri("/api/v1/info", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("product").GetString().ShouldBe("AutoCanvas");
        body.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Unknown_routes_answer_with_a_problem()
    {
        await using var factory = new AutoCanvasFactory();

        var response = await factory.CreateClient().GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("type").GetString().ShouldBe("https://bexy315.github.io/autocanvas/technical/api#not-found");
        problem.GetProperty("status").GetInt32().ShouldBe(404);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Unhandled_exceptions_become_a_problem_without_internals()
    {
        await using var factory = new AutoCanvasFactory().WithMiddleware(app => app.Run(context =>
            context.Request.Path == "/boom"
                ? throw new InvalidOperationException("connection string with password=hunter2")
                : Task.CompletedTask));

        var response = await factory.CreateClient().GetAsync(new Uri("/boom", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        text.ShouldNotContain("hunter2");
        var problem = JsonDocument.Parse(text).RootElement;
        problem.GetProperty("type").GetString().ShouldBe("https://bexy315.github.io/autocanvas/technical/api#internal");
    }

    [Fact]
    public async Task Liveness_runs_no_checks()
    {
        await using var factory = new AutoCanvasFactory().WithServices(services =>
            services.AddHealthChecks().AddCheck("broken", () => HealthCheckResult.Unhealthy(), ["ready"]));

        var response = await factory.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        body.GetProperty("checks").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task Readiness_fails_when_a_ready_check_fails_without_revealing_details()
    {
        await using var factory = new AutoCanvasFactory().WithServices(services => services.AddHealthChecks()
            .AddCheck("broken", () => HealthCheckResult.Unhealthy("Host=db;Password=hunter2"), ["ready"])
            .AddCheck("other", () => HealthCheckResult.Unhealthy()));

        var response = await factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var text = await response.Content.ReadAsStringAsync(Cancel);
        text.ShouldNotContain("hunter2");
        var body = JsonDocument.Parse(text).RootElement;
        body.GetProperty("status").GetString().ShouldBe("Unhealthy");
        body.GetProperty("checks").GetProperty("broken").GetString().ShouldBe("Unhealthy");
        body.GetProperty("checks").TryGetProperty("other", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Readiness_is_healthy_without_failing_checks()
    {
        await using var factory = new AutoCanvasFactory();

        var response = await factory.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Problems_set_by_endpoints_keep_their_own_type()
    {
        await using var factory = new AutoCanvasFactory().WithMiddleware(app => app.Run(context =>
            Results.Problem(type: "https://bexy315.github.io/autocanvas/technical/api#conflict", statusCode: 400).ExecuteAsync(context)));

        var response = await factory.CreateClient().GetAsync(new Uri("/anything", UriKind.Relative), Cancel);

        var problem = await ReadJsonAsync(response);
        problem.GetProperty("type").GetString().ShouldBe("https://bexy315.github.io/autocanvas/technical/api#conflict");
    }

    [Theory]
    [InlineData(400, "https://bexy315.github.io/autocanvas/technical/api#validation")]
    [InlineData(503, "https://bexy315.github.io/autocanvas/technical/api#internal")]
    [InlineData(418, null)]
    public async Task Status_codes_map_to_the_api_problem_types(int status, string? expectedType)
    {
        await using var factory = new AutoCanvasFactory().WithMiddleware(app => app.Run(context =>
            Results.Problem(statusCode: status).ExecuteAsync(context)));

        var response = await factory.CreateClient().GetAsync(new Uri("/anything", UriKind.Relative), Cancel);

        var problem = await ReadJsonAsync(response);
        (problem.TryGetProperty("type", out var type) ? type.GetString() : null).ShouldBe(expectedType);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel)).RootElement;
}
