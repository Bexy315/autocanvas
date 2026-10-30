using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Identity;

/// <summary><c>/api/v1/groups</c>: groups and their members, for instance admins.</summary>
internal static class GroupEndpoints
{
    public static void MapGroupEndpoints(this RouteGroupBuilder api)
    {
        var groups = api.MapGroup("/groups").WithTags("Groups").RequireAuthorization(AutoCanvasPolicies.Admin);

        groups.MapGet("/", ListAsync)
            .WithName("ListGroups")
            .Produces<Page<GroupView>>()
            .WithSummary("List groups")
            .WithDescription("Ordered by name with member counts, one page at a time (see API conventions: paging).")
            .ProducesValidationProblem();

        groups.MapPost("/", CreateAsync)
            .WithName("CreateGroup")
            .Produces<GroupDetails>(StatusCodes.Status201Created)
            .WithSummary("Create a group")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        groups.MapGet("/{id:guid}", GetAsync)
            .WithName("GetGroup")
            .Produces<GroupDetails>()
            .WithSummary("Get a group with its members")
            .ProducesProblem(StatusCodes.Status404NotFound);

        groups.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateGroup")
            .Produces<GroupDetails>()
            .WithSummary("Rename or describe a group")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        groups.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteGroup")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Delete a group")
            .WithDescription("Removes the group and its memberships; the users stay.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        groups.MapPut("/{id:guid}/members/{userId:guid}", AddMemberAsync)
            .WithName("AddGroupMember")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Add a user to a group")
            .WithDescription("Adding a member twice changes nothing.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        groups.MapDelete("/{id:guid}/members/{userId:guid}", RemoveMemberAsync)
            .WithName("RemoveGroupMember")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Remove a user from a group")
            .WithDescription("Removing a non-member changes nothing.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(int? limit, string? cursor, GroupService groups, CancellationToken cancellationToken) =>
        ApiResults.Ok(await groups.ListAsync(new PageRequest(limit, cursor), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(CreateGroupRequest request, GroupService groups, CancellationToken cancellationToken) =>
        ApiResults.Created(
            await groups.CreateAsync(new NewGroup(request.Name, request.Description), cancellationToken).ConfigureAwait(false),
            group => $"/api/v1/groups/{group.Id}");

    private static async Task<IResult> GetAsync(Guid id, GroupService groups, CancellationToken cancellationToken) =>
        ApiResults.Ok(await groups.GetAsync(id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UpdateAsync(Guid id, UpdateGroupRequest request, GroupService groups, CancellationToken cancellationToken) =>
        ApiResults.Ok(await groups.UpdateAsync(id, new GroupChanges { Name = request.Name, Description = request.Description }, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> DeleteAsync(Guid id, GroupService groups, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await groups.DeleteAsync(id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> AddMemberAsync(Guid id, Guid userId, GroupService groups, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await groups.AddMemberAsync(id, userId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> RemoveMemberAsync(Guid id, Guid userId, GroupService groups, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await groups.RemoveMemberAsync(id, userId, cancellationToken).ConfigureAwait(false));
}
