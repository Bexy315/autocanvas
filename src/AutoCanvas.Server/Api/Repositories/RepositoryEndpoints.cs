using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Repositories;

/// <summary>
/// Repositories connected in folders (ADR-0021): <c>/api/v1/folders/{id}/repositories</c> and
/// <c>/api/v1/repositories/{id}</c>.
/// </summary>
internal static class RepositoryEndpoints
{
    public static void MapRepositoryEndpoints(this RouteGroupBuilder api)
    {
        var inFolder = api.MapGroup("/folders/{id:guid}/repositories").WithTags("Repositories").RequireAuthorization();

        inFolder.MapGet("/", ListAsync)
            .WithName("ListRepositories")
            .Produces<IReadOnlyList<RepositoryView>>()
            .WithSummary("List a folder's repositories")
            .WithDescription("The repositories connected in the folder, by name, with the state of their last sync. Needs Viewer.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        inFolder.MapPost("/", ConnectAsync)
            .WithName("ConnectRepository")
            .Produces<RepositoryView>(StatusCodes.Status201Created)
            .WithSummary("Connect a Git repository")
            .WithDescription("Connects a Git repository reachable over http or https in the folder and reads `.autocanvas/automations/*.yml` at its default branch (or `defaultBranch`): each valid file becomes a repository automation of the folder. A repository that cannot be read is refused with git's reason. `secret` (a token or password) is stored encrypted and never shown again. Needs Editor.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var repositories = api.MapGroup("/repositories/{id:guid}").WithTags("Repositories").RequireAuthorization();

        repositories.MapGet("/", GetAsync)
            .WithName("GetRepository")
            .Produces<RepositoryView>()
            .WithSummary("Read a repository")
            .WithDescription("The repository with the state of its last sync and the files that are not automations, and why. Never its credential. Needs Viewer.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        repositories.MapPatch("/", UpdateAsync)
            .WithName("UpdateRepository")
            .Produces<RepositoryView>()
            .WithSummary("Change a repository")
            .WithDescription("Renames it, chooses another branch (empty for the repository's default) or credential (an empty `secret` removes it), then reads it again. Fields left out stay as they are. Needs Editor.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        repositories.MapDelete("/", DisconnectAsync)
            .WithName("DisconnectRepository")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Disconnect a repository")
            .WithDescription("Removes the repository from its folder and deletes its automations; their runs stay. Nothing in the repository changes. Needs Editor.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        repositories.MapPost("/sync", SyncAsync)
            .WithName("SyncRepository")
            .Produces<RepositoryView>()
            .WithSummary("Read a repository again")
            .WithDescription("Reads the automation files at the repository's branch now: new files become automations, changed ones new versions at the commit, removed ones are deleted. When the repository cannot be read, nothing changes and `lastError` says why. Needs Operator.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(Guid id, HttpContext context, RepositoryService repositories, CancellationToken cancellationToken) =>
        ApiResults.Ok(await repositories.ListAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ConnectAsync(Guid id, NewRepository request, HttpContext context, RepositoryService repositories, CancellationToken cancellationToken) =>
        ApiResults.Created(await repositories.ConnectAsync(context.User.ToActor(), id, request, cancellationToken).ConfigureAwait(false), view => $"/api/v1/repositories/{view.Id}");

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, RepositoryService repositories, CancellationToken cancellationToken) =>
        ApiResults.Ok(await repositories.GetAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UpdateAsync(Guid id, RepositoryChange change, HttpContext context, RepositoryService repositories, CancellationToken cancellationToken) =>
        ApiResults.Ok(await repositories.UpdateAsync(context.User.ToActor(), id, change, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> DisconnectAsync(Guid id, HttpContext context, RepositoryService repositories, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await repositories.DisconnectAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> SyncAsync(Guid id, HttpContext context, RepositoryService repositories, CancellationToken cancellationToken) =>
        ApiResults.Ok(await repositories.SyncAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));
}
