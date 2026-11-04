using AutoCanvas.Server.Api.Triggers;
using AutoCanvas.Server.Api.Audit;
using AutoCanvas.Server.Api.Automations;
using AutoCanvas.Server.Api.Folders;
using AutoCanvas.Server.Api.Forges;
using AutoCanvas.Server.Api.Identity;
using AutoCanvas.Server.Api.Instance;
using AutoCanvas.Server.Api.Nodes;
using AutoCanvas.Server.Api.Runners;
using AutoCanvas.Server.Api.Repositories;
using AutoCanvas.Server.Api.Runs;
using AutoCanvas.Server.Api.Setup;
using AutoCanvas.Server.Api.Variables;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace AutoCanvas.Server.Api;

/// <summary>The versioned HTTP API under <c>/api/v1</c> and its OpenAPI description.</summary>
internal static partial class ApiEndpoints
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddAutoCanvasOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options => options.AddOperationTransformer(DeclareRouteValues).AddDocumentTransformer((document, _, _) =>
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

    /// <summary>
    /// Declares every value of an operation's route as a required path parameter. A handler shared by
    /// several routes (the variables of the instance and of a folder) reads the folder's id itself, so
    /// ASP.NET Core does not see it; clients still need to know it is part of the path.
    /// </summary>
    private static Task DeclareRouteValues(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (Match match in RouteValue().Matches(context.Description.RelativePath ?? string.Empty))
        {
            var name = match.Groups["name"].Value;
            operation.Parameters ??= [];
            if (operation.Parameters.Any(parameter => parameter.In == ParameterLocation.Path && string.Equals(parameter.Name, name, StringComparison.Ordinal)))
            {
                continue;
            }

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Path,
                Required = true,
                // Every {id} of the API is a GUID; other route values are text.
                Schema = string.Equals(name, "id", StringComparison.Ordinal)
                    ? new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }
                    : new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex(@"\{(?<name>[A-Za-z]+)(?::[^}]*)?\}", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RouteValue();

    public static IEndpointRouteBuilder MapAutoCanvasApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapGet("/info", GetInfo)
            .WithName("GetServerInfo")
            .WithSummary("Describe this server")
            .WithDescription("Product name and version. Needs no authentication, so clients can check that they talk to an AutoCanvas server.")
            .WithTags("Server")
            .AllowAnonymous();

        api.MapSetupEndpoints();
        api.MapAuthEndpoints();
        api.MapMeEndpoints();
        api.MapUserEndpoints();
        api.MapGroupEndpoints();
        api.MapDirectoryEndpoints();
        api.MapFolderEndpoints();
        api.MapAutomationEndpoints();
        api.MapNodeTypeEndpoints();
        api.MapVariableEndpoints();
        api.MapAuditEndpoints();
        api.MapRunEndpoints();
        api.MapReportEndpoints();
        api.MapWebhookEndpoints();
        api.MapRunnerEndpoints();
        api.MapInstanceEndpoints();
        api.MapRepositoryEndpoints();
        api.MapGitHubEndpoints();

        return endpoints;
    }

    private static ServerInfo GetInfo() => ServerInfo.Current;
}
