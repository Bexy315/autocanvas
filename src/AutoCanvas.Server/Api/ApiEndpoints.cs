using AutoCanvas.Server.Api.Triggers;
using AutoCanvas.Server.Api.Audit;
using AutoCanvas.Server.Api.Automations;
using AutoCanvas.Server.Api.Folders;
using AutoCanvas.Server.Api.Identity;
using AutoCanvas.Server.Api.Nodes;
using AutoCanvas.Server.Api.Runners;
using AutoCanvas.Server.Api.Runs;
using AutoCanvas.Server.Api.Variables;
using Microsoft.OpenApi;

namespace AutoCanvas.Server.Api;

/// <summary>The versioned HTTP API under <c>/api/v1</c> and its OpenAPI description.</summary>
internal static class ApiEndpoints
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddAutoCanvasOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "AutoCanvas API",
                Version = DocumentName,
                Description = "HTTP API of the AutoCanvas server. Conventions: https://bexy315.github.io/autocanvas/technical/api",
                License = new OpenApiLicense { Name = "MIT", Identifier = "MIT" },
            };

            // Relative to wherever the server is reached, so the document does not depend on the host.
            document.Servers = [new OpenApiServer { Url = "/" }];
            return Task.CompletedTask;
        }));

    public static IEndpointRouteBuilder MapAutoCanvasApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapGet("/info", GetInfo)
            .WithName("GetServerInfo")
            .WithSummary("Describe this server")
            .WithDescription("Product name and version. Needs no authentication, so clients can check that they talk to an AutoCanvas server.")
            .WithTags("Server")
            .AllowAnonymous();

        api.MapAuthEndpoints();
        api.MapMeEndpoints();
        api.MapUserEndpoints();
        api.MapGroupEndpoints();
        api.MapFolderEndpoints();
        api.MapAutomationEndpoints();
        api.MapNodeTypeEndpoints();
        api.MapVariableEndpoints();
        api.MapAuditEndpoints();
        api.MapRunEndpoints();
        api.MapReportEndpoints();
        api.MapWebhookEndpoints();
        api.MapRunnerEndpoints();

        return endpoints;
    }

    private static ServerInfo GetInfo() => ServerInfo.Current;
}
