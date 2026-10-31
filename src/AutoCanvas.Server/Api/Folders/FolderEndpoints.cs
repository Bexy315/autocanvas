using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Folders;

/// <summary>
/// <c>/api/v1/folders</c>: the folder tree. Every endpoint needs a signed-in user; what they may do
/// depends on their role on the folder (ADR-0012), which the use cases check.
/// </summary>
internal static class FolderEndpoints
{
    public static void MapFolderEndpoints(this RouteGroupBuilder api)
    {
        var folders = api.MapGroup("/folders").WithTags("Folders").RequireAuthorization();

        folders.MapGet("/", ListEntryPointsAsync)
            .WithName("ListFolderEntryPoints")
            .WithSummary("List where I can start")
            .WithDescription("The topmost folders the caller may view, each with the names of the folders above it. For admins: the top-level folders.");

        folders.MapPost("/", CreateAsync)
            .WithName("CreateFolder")
            .Produces<FolderView>(StatusCodes.Status201Created)
            .WithSummary("Create a folder")
            .WithDescription("At the top level anyone may create a folder and becomes its Owner; a subfolder needs Editor on the parent and inherits from it.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        folders.MapGet("/{id:guid}", GetAsync)
            .WithName("GetFolder")
            .Produces<FolderView>()
            .WithSummary("Get a folder")
            .WithDescription("With the caller's role and the folders above it. The ETag header is the folder's version, for If-Match.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        folders.MapGet("/{id:guid}/children", ListChildrenAsync)
            .WithName("ListFolderChildren")
            .Produces<Page<FolderSummary>>()
            .WithSummary("List subfolders")
            .WithDescription("Ordered by name, one page at a time. Restricted subfolders appear only to callers with a role on them.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        MapChangeEndpoints(folders);
        MapGrantEndpoints(folders);
    }

    private static void MapChangeEndpoints(RouteGroupBuilder folders)
    {
        folders.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateFolder")
            .Produces<FolderView>()
            .WithSummary("Rename or describe a folder")
            .WithDescription("Needs Editor. Send If-Match with the ETag to avoid overwriting someone else's change (412).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        folders.MapPost("/{id:guid}/move", MoveAsync)
            .WithName("MoveFolder")
            .Produces<FolderView>()
            .WithSummary("Move a folder")
            .WithDescription("With everything below it. Needs Editor on the folder and on the new parent, or Owner to move it to the top level. Rights change with the place.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        folders.MapPut("/{id:guid}/restriction", SetRestrictionAsync)
            .WithName("SetFolderRestriction")
            .Produces<FolderView>()
            .WithSummary("Restrict a folder or lift that")
            .WithDescription("Needs Owner. In a restricted folder, grants from above no longer apply; the caller keeps access through an explicit Owner grant.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

        folders.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteFolder")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Delete a folder")
            .WithDescription("Needs Owner, and the folder must be empty (409 otherwise). Its grants go with it.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
    }

    private static void MapGrantEndpoints(RouteGroupBuilder folders)
    {
        folders.MapGet("/{id:guid}/grants", ListGrantsAsync)
            .WithName("ListFolderGrants")
            .Produces<IReadOnlyList<GrantView>>()
            .WithSummary("List the grants on a folder")
            .WithDescription("Roles given directly on this folder, users first. Needs Owner.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        folders.MapPut("/{id:guid}/grants", SetGrantAsync)
            .WithName("SetFolderGrant")
            .Produces<GrantView>()
            .WithSummary("Grant a role on a folder")
            .WithDescription("To a user or a group; replaces the role they had on this folder. Needs Owner.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        folders.MapDelete("/{id:guid}/grants/{grantId:guid}", RemoveGrantAsync)
            .WithName("RemoveFolderGrant")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Remove a grant")
            .WithDescription("Needs Owner. Removing your own Owner grant can lock you out; an admin can repair that.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IReadOnlyList<FolderView>> ListEntryPointsAsync(HttpContext context, FolderService folders, CancellationToken cancellationToken) =>
        await folders.ListEntryPointsAsync(context.User.ToActor(), cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> CreateAsync(CreateFolderRequest request, HttpContext context, FolderService folders, CancellationToken cancellationToken)
    {
        var result = await folders.CreateAsync(context.User.ToActor(), new NewFolder(request.Name, request.ParentId, request.Description), cancellationToken).ConfigureAwait(false);
        return ApiResults.Created(result, folder => $"/api/v1/folders/{folder.Id}").WithETag(context, result, f => f.Version);
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, FolderService folders, CancellationToken cancellationToken)
    {
        var result = await folders.GetAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, f => f.Version);
    }

    private static async Task<IResult> ListChildrenAsync(Guid id, int? limit, string? cursor, HttpContext context, FolderService folders, CancellationToken cancellationToken) =>
        ApiResults.Ok(await folders.ListChildrenAsync(context.User.ToActor(), id, new PageRequest(limit, cursor), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UpdateAsync(Guid id, UpdateFolderRequest request, HttpContext context, FolderService folders, CancellationToken cancellationToken)
    {
        var result = await folders.UpdateAsync(
            context.User.ToActor(),
            id,
            new FolderChanges { Name = request.Name, Description = request.Description },
            EntityTags.ExpectedVersion(context.Request),
            cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, f => f.Version);
    }

    private static async Task<IResult> MoveAsync(Guid id, MoveFolderRequest request, HttpContext context, FolderService folders, CancellationToken cancellationToken)
    {
        var result = await folders.MoveAsync(context.User.ToActor(), id, request.ParentId, EntityTags.ExpectedVersion(context.Request), cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, f => f.Version);
    }

    private static async Task<IResult> SetRestrictionAsync(Guid id, SetRestrictionRequest request, HttpContext context, FolderService folders, CancellationToken cancellationToken)
    {
        var result = await folders.SetRestrictedAsync(context.User.ToActor(), id, request.IsRestricted, EntityTags.ExpectedVersion(context.Request), cancellationToken).ConfigureAwait(false);
        return ApiResults.Ok(result).WithETag(context, result, f => f.Version);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, FolderService folders, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await folders.DeleteAsync(context.User.ToActor(), id, EntityTags.ExpectedVersion(context.Request), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ListGrantsAsync(Guid id, HttpContext context, FolderGrantService grants, CancellationToken cancellationToken) =>
        ApiResults.Ok(await grants.ListAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> SetGrantAsync(Guid id, SetGrantRequest request, HttpContext context, FolderGrantService grants, CancellationToken cancellationToken) =>
        ApiResults.Ok(await grants.SetAsync(context.User.ToActor(), id, request.UserId, request.GroupId, request.Role, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RemoveGrantAsync(Guid id, Guid grantId, HttpContext context, FolderGrantService grants, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await grants.RemoveAsync(context.User.ToActor(), id, grantId, cancellationToken).ConfigureAwait(false));
}
