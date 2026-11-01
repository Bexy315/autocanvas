using System.Net;

namespace AutoCanvas.Server.Tests;

/// <summary>The server serves the built web UI from its web root (ADR-0018).</summary>
public sealed class WebUiTests : IDisposable
{
    private const string Index = "<!doctype html><title>AutoCanvas</title>";
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("autocanvas-web-");

    public WebUiTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, "index.html"), Index);
        Directory.CreateDirectory(Path.Combine(root.FullName, "assets"));
        File.WriteAllText(Path.Combine(root.FullName, "assets", "app-1a2b3c.js"), "console.log('ui');");
        File.WriteAllText(Path.Combine(root.FullName, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/")]
    [InlineData("/automations/42")]
    [InlineData("/sign-in?returnTo=%2F")]
    public async Task Paths_of_the_UI_get_its_page_with_a_content_security_policy(string path)
    {
        await using var factory = new AutoCanvasFactory().WithWebRoot(root.FullName);

        var response = await factory.CreateClient().GetAsync(new Uri(path, UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Cancel)).ShouldBe(Index);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        response.Headers.CacheControl?.NoCache.ShouldBeTrue();
        response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldContain("script-src 'self'");
        response.Headers.GetValues("X-Content-Type-Options").ShouldHaveSingleItem().ShouldBe("nosniff");
    }

    [Fact]
    public async Task Hashed_assets_are_cached_for_good_and_other_files_are_checked_on_each_load()
    {
        await using var factory = new AutoCanvasFactory().WithWebRoot(root.FullName);
        var client = factory.CreateClient();

        var asset = await client.GetAsync(new Uri("/assets/app-1a2b3c.js", UriKind.Relative), Cancel);
        var icon = await client.GetAsync(new Uri("/favicon.svg", UriKind.Relative), Cancel);

        asset.StatusCode.ShouldBe(HttpStatusCode.OK);
        asset.Headers.CacheControl?.ToString().ShouldBe("public, max-age=31536000, immutable");
        icon.StatusCode.ShouldBe(HttpStatusCode.OK);
        icon.Headers.CacheControl?.NoCache.ShouldBeTrue();
        icon.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
    }

    [Theory]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData("/health/nope")]
    [InlineData("/openapi/v1/nope")]
    public async Task Unknown_server_paths_stay_problems_instead_of_the_UI(string path)
    {
        await using var factory = new AutoCanvasFactory().WithWebRoot(root.FullName);

        var response = await factory.CreateClient().GetAsync(new Uri(path, UriKind.Relative), Cancel);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await response.JsonAsync()).GetProperty("type").GetString().ShouldBe("https://bexy315.github.io/autocanvas/technical/api#not-found");
    }

    [Fact]
    public async Task Only_reading_requests_fall_back_to_the_page()
    {
        await using var factory = new AutoCanvasFactory().WithWebRoot(root.FullName);

        var response = await factory.CreateClient().PostAsync(new Uri("/automations", UriKind.Relative), null, Cancel);

        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_a_built_UI_the_server_answers_its_paths_with_not_found()
    {
        var empty = Directory.CreateTempSubdirectory("autocanvas-noweb-");
        try
        {
            await using var factory = new AutoCanvasFactory().WithWebRoot(empty.FullName);
            var client = factory.CreateClient();

            (await client.GetAsync(new Uri("/", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync(new Uri("/api/v1/info", UriKind.Relative), Cancel)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    public void Dispose() => root.Delete(recursive: true);
}
