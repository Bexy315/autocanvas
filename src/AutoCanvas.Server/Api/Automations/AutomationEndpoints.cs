using System.Text;
using AutoCanvas.Server.Application.Automations;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Triggers;
using AutoCanvas.Server.Authentication;
using Microsoft.Net.Http.Headers;

namespace AutoCanvas.Server.Api.Automations;

/// <summary>
/// Managed automations (ADR-0003): <c>/api/v1/folders/{id}/automations</c> to list and create,
/// <c>/api/v1/automations/{id}</c> for one automation and its versions. The folder's roles decide
/// access (ADR-0012): Viewer reads, Editor changes. The ETag is the number of the current version.
/// </summary>
internal static class AutomationEndpoints
{
    public const string YamlContentType = "text/yaml";

    public static void MapAutomationEndpoints(this RouteGroupBuilder api)
    {
        var inFolder = api.MapGroup("/folders/{folderId:guid}/automations").WithTags("Automations").RequireAuthorization();

        inFolder.MapGet("/", ListAsync)
            .WithName("ListAutomations")
            .Produces<Page<AutomationSummary>>()
            .WithSummary("List the automations in a folder")
            .WithDescription("Ordered by name, one page at a time. Needs Viewer on the folder; subfolders are listed separately.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        inFolder.MapPost("/", CreateAsync)
            .WithName("CreateAutomation")
            .Produces<AutomationView>(StatusCodes.Status201Created)
            .WithSummary("Create an automation")
            .WithDescription("Saves the file as version 1. Needs Editor on the folder. An invalid file is refused with every problem in `diagnostics`.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var automations = api.MapGroup("/automations").WithTags("Automations").RequireAuthorization();

        MapValidateEndpoint(automations);

        automations.MapGet("/{id:guid}", GetAsync)
            .WithName("GetAutomation")
            .Produces<AutomationView>()
            .WithSummary("Get an automation")
            .WithDescription("With the file of the current version, its warnings and the caller's role. The ETag header is the version number, for If-Match.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        automations.MapGet("/{id:guid}/yaml", GetYamlAsync)
            .WithName("GetAutomationYaml")
            .Produces<string>(contentType: YamlContentType)
            .WithSummary("Download the automation file")
            .WithDescription("The current version exactly as saved, as `text/yaml` with the automation's name as file name.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        automations.MapGet("/{id:guid}/triggers", GetTriggersAsync)
            .WithName("GetAutomationTriggers")
            .Produces<AutomationTriggersView>()
            .WithSummary("Get what starts an automation")
            .WithDescription("From its current version: whether it starts by hand or by webhook, its schedule entries with when each fires next, and the automation it follows. Needs Viewer.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        MapChangeEndpoints(automations);

        MapVersionEndpoints(automations);
    }

    private static void MapVersionEndpoints(RouteGroupBuilder automations)
    {
        automations.MapGet("/{id:guid}/versions", ListVersionsAsync)
            .WithName("ListAutomationVersions")
            .Produces<Page<AutomationVersionSummary>>()
            .WithSummary("List the versions of an automation")
            .WithDescription("Newest first, one page at a time, with message and author.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        automations.MapGet("/{id:guid}/versions/{number:int}", GetVersionAsync)
            .WithName("GetAutomationVersion")
            .Produces<AutomationVersionView>()
            .WithSummary("Get one version of an automation")
            .WithDescription("With its file. To restore it, save that file again as a new version.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapValidateEndpoint(RouteGroupBuilder automations) =>
        automations.MapPost("/validate", ValidateAsync)
            .WithName("ValidateAutomation")
            .Produces<AutomationValidationView>()
            .WithSummary("Check an automation file without saving it")
            .WithDescription("Runs the checks a save runs, in the given folder: the file, its node types and its triggers. Answers 200 with every error and warning, also when the file is invalid; `valid` says whether a save would accept it. Needs Viewer on the folder.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static void MapChangeEndpoints(RouteGroupBuilder automations)
    {
        automations.MapPut("/{id:guid}", SaveAsync)
            .WithName("SaveAutomation")
            .Produces<AutomationView>()
            .WithSummary("Save a new version")
            .WithDescription("Needs Editor. The file must be valid (else `diagnostics` lists every problem) and differ from the current version. Send If-Match with the ETag to avoid overwriting someone else's change (412).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        automations.MapPatch("/{id:guid}", RenameAsync)
            .WithName("RenameAutomation")
            .Produces<AutomationView>()
            .WithSummary("Rename an automation")
            .WithDescription("Changes its key in the folder; the versions stay. Needs Editor.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        automations.MapPost("/{id:guid}/move", MoveAsync)
            .WithName("MoveAutomation")
            .Produces<AutomationView>()
            .WithSummary("Move an automation to another folder")
            .WithDescription("Needs Editor on both folders, and the name must be free in the new one. Rights change with the place.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        automations.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteAutomation")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Delete an automation")
            .WithDescription("With all its versions; this cannot be undone. Needs Editor. The audit log keeps who deleted what.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
    }

    private static async Task<IResult> ValidateAsync(ValidateAutomationRequest request, HttpContext context, AutomationService automations, CancellationToken cancellationToken) =>
        ApiResults.Ok(await automations.ValidateAsync(context.User.ToActor(), request.FolderId, request.AutomationId, request.Yaml, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ListAsync(Guid folderId, int? limit, string? cursor, HttpContext context, AutomationService automations, CancellationToken cancellationToken) =>
        ApiResults.Ok(await automations.ListAsync(context.User.ToActor(), folderId, new PageRequest(limit, cursor), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(Guid folderId, CreateAutomationRequest request, HttpContext context, AutomationService automations, CancellationToken cancellationToken)
    {
        var result = await automations.CreateAsync(context.User.ToActor(), folderId, request.Name, request.Yaml, request.Message, cancellationToken).ConfigureAwait(false);
        return ApiResults.Created(result, a => $"/api/v1/automations/{a.Id}").WithETag(context, result, a => a.CurrentVersion);
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, AutomationService automations, CancellationToken cancellationToken)
    {
        var result = await automations.GetAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, a => a.CurrentVersion);
    }

    private static async Task<IResult> GetYamlAsync(Guid id, HttpContext context, AutomationService automations, CancellationToken cancellationToken)
    {
        var result = await automations.GetAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            return ApiResults.Problem(error);
        }

        var automation = result.Value!;
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName($"{automation.Name}.yml");
        context.Response.Headers.ContentDisposition = disposition.ToString();
        context.Response.Headers.ETag = EntityTags.Format(automation.CurrentVersion);
        return TypedResults.Text(automation.Yaml, YamlContentType, Encoding.UTF8);
    }

    private static async Task<IResult> SaveAsync(Guid id, SaveAutomationRequest request, HttpContext context, AutomationService automations, CancellationToken cancellationToken)
    {
        var result = await automations.SaveAsync(context.User.ToActor(), id, request.Yaml, request.Message, EntityTags.ExpectedVersion(context.Request), cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, a => a.CurrentVersion);
    }

    private static async Task<IResult> RenameAsync(Guid id, RenameAutomationRequest request, HttpContext context, AutomationService automations, CancellationToken cancellationToken)
    {
        var result = await automations.RenameAsync(context.User.ToActor(), id, request.Name, EntityTags.ExpectedVersion(context.Request), cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, a => a.CurrentVersion);
    }

    private static async Task<IResult> MoveAsync(Guid id, MoveAutomationRequest request, HttpContext context, AutomationService automations, CancellationToken cancellationToken)
    {
        var result = await automations.MoveAsync(context.User.ToActor(), id, request.FolderId, EntityTags.ExpectedVersion(context.Request), cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, a => a.CurrentVersion);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, AutomationService automations, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await automations.DeleteAsync(context.User.ToActor(), id, EntityTags.ExpectedVersion(context.Request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ListVersionsAsync(Guid id, int? limit, string? cursor, HttpContext context, AutomationService automations, CancellationToken cancellationToken) =>
        ApiResults.Ok(await automations.ListVersionsAsync(context.User.ToActor(), id, new PageRequest(limit, cursor), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetVersionAsync(Guid id, int number, HttpContext context, AutomationService automations, CancellationToken cancellationToken) =>
        ApiResults.Ok(await automations.GetVersionAsync(context.User.ToActor(), id, number, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetTriggersAsync(Guid id, HttpContext context, AutomationTriggerService triggers, CancellationToken cancellationToken) =>
        ApiResults.Ok(await triggers.GetAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));
}
