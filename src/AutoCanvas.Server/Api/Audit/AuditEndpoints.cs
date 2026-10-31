using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Audit;

/// <summary>
/// Reading the audit log: <c>/api/v1/audit</c> for admins, <c>/api/v1/folders/{id}/audit</c> for
/// the folder's owners. Newest first; filters combine.
/// </summary>
internal static class AuditEndpoints
{
    private const string Filters =
        "Filters: `from` and `to` (date and time, `to` exclusive), `action` (exact, or a prefix ending in `*` such as `folder.*`), `actorId`, `targetType`, `targetId`.";

    public static void MapAuditEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/audit", ListAsync)
            .WithTags("Audit")
            .RequireAuthorization()
            .WithName("ListAuditEvents")
            .Produces<Page<AuditEventView>>()
            .WithSummary("Read the audit log")
            .WithDescription($"Every recorded change, newest first, one page at a time. Admins only. {Filters}")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        api.MapGet("/folders/{id:guid}/audit", ListForFolderAsync)
            .WithTags("Audit")
            .RequireAuthorization()
            .WithName("ListFolderAuditEvents")
            .Produces<Page<AuditEventView>>()
            .WithSummary("Read a folder's audit log")
            .WithDescription($"Changes in the folder and in the folders below it that the caller owns too, newest first. Needs Owner. {Filters}")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ListAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? action,
        Guid? actorId,
        string? targetType,
        string? targetId,
        int? limit,
        string? cursor,
        HttpContext context,
        AuditQueryService audit,
        CancellationToken cancellationToken) =>
        ApiResults.Ok(await audit.ListAsync(
            context.User.ToActor(),
            new AuditFilter(from, to, action, actorId, targetType, targetId),
            new PageRequest(limit, cursor),
            cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ListForFolderAsync(
        Guid id,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? action,
        Guid? actorId,
        string? targetType,
        string? targetId,
        int? limit,
        string? cursor,
        HttpContext context,
        AuditQueryService audit,
        CancellationToken cancellationToken) =>
        ApiResults.Ok(await audit.ListForFolderAsync(
            context.User.ToActor(),
            id,
            new AuditFilter(from, to, action, actorId, targetType, targetId),
            new PageRequest(limit, cursor),
            cancellationToken).ConfigureAwait(false));
}
