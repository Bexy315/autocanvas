using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AutoCanvas.Server.Tests;

/// <summary>
/// Guards the public API contract: the committed OpenAPI document matches the server, and every
/// endpoint follows the conventions in <c>src/AutoCanvas.Server/AGENTS.md</c>.
/// </summary>
public sealed class ApiContractTests
{
    private const string UpdateVariable = "AUTOCANVAS_UPDATE_SNAPSHOTS";
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task The_committed_OpenAPI_document_matches_the_server()
    {
        await using var factory = new AutoCanvasFactory();
        var served = await factory.CreateClient().GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        // The compiler writes multi-line XML docs with the platform's line ending, which ends up in
        // descriptions; compare them with "\n" so the snapshot is the same on Windows and Linux.
        var expected = JsonNode.Parse(served)!.ToJsonString(Indented).ReplaceLineEndings("\n").Replace("\\r\\n", "\\n", StringComparison.Ordinal) + "\n";

        var path = Path.Combine(RepositoryRoot(), "openapi", "v1.json");
        var committed = File.Exists(path) ? (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n") : null;

        if (string.Equals(committed, expected, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "1", StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(path, expected, TestContext.Current.CancellationToken);
            return;
        }

        Assert.Fail($"openapi/v1.json is out of date. Run the tests with {UpdateVariable}=1 to update it, then review and commit the change.\n{FirstDifference(committed, expected)}");
    }

    [Fact]
    public async Task Every_api_endpoint_is_named_described_and_declares_who_may_call_it()
    {
        await using var factory = new AutoCanvasFactory();
        _ = factory.Server;

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToList();

        endpoints.ShouldNotBeEmpty();
        endpoints.ShouldAllBe(e =>
            e.Metadata.GetMetadata<IEndpointNameMetadata>() != null
            && e.Metadata.GetMetadata<IEndpointSummaryMetadata>() != null
            && (e.Metadata.GetMetadata<IAllowAnonymous>() != null || e.Metadata.GetMetadata<IAuthorizeData>() != null));
    }

    // Names the first line that differs, so a mismatch that only happens elsewhere (e.g. in CI) can be understood from the log.
    private static string FirstDifference(string? committed, string served)
    {
        var expected = (committed ?? string.Empty).Split('\n');
        var actual = served.Split('\n');
        for (var line = 0; line < Math.Max(expected.Length, actual.Length); line++)
        {
            var left = line < expected.Length ? expected[line] : "<end of file>";
            var right = line < actual.Length ? actual[line] : "<end of file>";
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                return $"First difference in line {line + 1}:\n  committed: {left}\n  served:    {right}";
            }
        }

        return "The documents differ only in line endings.";
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AutoCanvas.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("AutoCanvas.slnx not found above the test output directory.");
    }
}
