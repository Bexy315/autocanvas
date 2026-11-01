using AutoCanvas.Server.Api;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;

namespace AutoCanvas.Server.Hosting;

/// <summary>
/// Serves the web UI from <c>wwwroot</c> on the API's own origin (ADR-0018): its files, and
/// <c>index.html</c> for every other path the browser asks for, so the UI's routes work on reload.
/// Without a built UI the server works as before and answers those paths with 404.
/// </summary>
internal static class WebUi
{
    /// <summary>
    /// What the UI may load: only its own files and its own API. <c>style-src</c> allows inline
    /// styles because React and the canvas position elements with <c>style</c> attributes; scripts
    /// stay limited to the UI's own files.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>Vite puts every file whose name carries a content hash here.</summary>
    private const string AssetsPath = "/assets";

    private const string IndexFile = "index.html";

    /// <summary>Paths that belong to the server, never to the UI: an unknown one is a 404, not the UI's page.</summary>
    private static readonly string[] ServerPaths = ["/api", "/health", "/openapi"];

    /// <summary>Headers for every response, then the UI's static files.</summary>
    public static WebApplication UseAutoCanvasWebUi(this WebApplication app)
    {
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers["Referrer-Policy"] = "same-origin";
            return next(context);
        });
        app.UseStaticFiles(FileOptions());
        return app;
    }

    /// <summary>
    /// The fallbacks, when a UI is built: <c>index.html</c> for the UI's routes, and a problem for
    /// unknown server paths, which would otherwise get that page too. Without a UI nothing is mapped,
    /// so unknown paths answer exactly as before.
    /// </summary>
    public static WebApplication MapAutoCanvasWebUi(this WebApplication app)
    {
        if (!app.Environment.WebRootFileProvider.GetFileInfo(IndexFile).Exists)
        {
            return app;
        }

        foreach (var path in ServerPaths)
        {
            var name = path.TrimStart('/');
            app.MapFallback($"{path}/{{**rest}}", NotFound)
                .WithName($"NotFound-{name}")
                .WithSummary($"Unknown {name} path")
                .AllowAnonymous()
                .ExcludeFromDescription();
        }

        app.MapFallbackToFile(IndexFile, FileOptions())
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]))
            .AllowAnonymous();
        return app;
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        type: ProblemTypes.NotFound,
        title: "Not Found",
        detail: "There is nothing at this address.");

    private static StaticFileOptions FileOptions() => new()
    {
        OnPrepareResponse = context =>
        {
            var response = context.Context.Response;
            if (context.Context.Request.Path.StartsWithSegments(AssetsPath, StringComparison.Ordinal))
            {
                // The name changes with the content, so a file under this name never changes.
                response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return;
            }

            // Everything else, index.html above all, is checked on each load so a new version shows at once.
            response.Headers.CacheControl = "no-cache";
            if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
            }
        },
    };
}
