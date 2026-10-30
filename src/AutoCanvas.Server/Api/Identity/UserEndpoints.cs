using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Identity;

/// <summary><c>/api/v1/users</c>: account administration for instance admins.</summary>
internal static class UserEndpoints
{
    public static void MapUserEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/users").WithTags("Users").RequireAuthorization(AutoCanvasPolicies.Admin);

        users.MapGet("/", ListAsync)
            .WithName("ListUsers")
            .Produces<Page<UserView>>()
            .WithSummary("List users")
            .WithDescription("Ordered by user name, one page at a time (see API conventions: paging).")
            .ProducesValidationProblem();

        users.MapPost("/", CreateAsync)
            .WithName("CreateUser")
            .Produces<UserView>(StatusCodes.Status201Created)
            .WithSummary("Create a user")
            .WithDescription("The user must change the given password at the first sign-in.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapGet("/{id:guid}", GetAsync)
            .WithName("GetUser")
            .Produces<UserView>()
            .WithSummary("Get a user")
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPatch("/{id:guid}", UpdateAsync)
            .WithName("UpdateUser")
            .Produces<UserView>()
            .WithSummary("Change a user")
            .WithDescription("Display name, e-mail, admin and disabled. The last enabled admin cannot be demoted or disabled (409).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteUser")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Delete a user")
            .WithDescription("Removes the account with its sessions, tokens and memberships; the audit log keeps the name. Not the last enabled admin (409).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPost("/{id:guid}/password-reset", ResetPasswordAsync)
            .WithName("ResetUserPassword")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Set a user's password")
            .WithDescription("The user must change it at the next sign-in; their sessions end.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        MapTokenEndpoints(users);
    }

    private static void MapTokenEndpoints(RouteGroupBuilder users)
    {
        users.MapGet("/{id:guid}/tokens", ListTokensAsync)
            .WithName("ListUserTokens")
            .Produces<IReadOnlyList<TokenView>>()
            .WithSummary("List a user's API tokens")
            .WithDescription("Newest first, without the secrets.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapDelete("/{id:guid}/tokens/{tokenId:guid}", DeleteTokenAsync)
            .WithName("DeleteUserToken")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Delete a user's API token")
            .WithDescription("For example when a token has leaked. Requests with it fail from now on.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListTokensAsync(Guid id, TokenService tokens, CancellationToken cancellationToken) =>
        ApiResults.Ok(await tokens.ListAsync(id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> DeleteTokenAsync(Guid id, Guid tokenId, TokenService tokens, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await tokens.DeleteAsync(id, tokenId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ListAsync(int? limit, string? cursor, UserService users, CancellationToken cancellationToken) =>
        ApiResults.Ok(await users.ListAsync(new PageRequest(limit, cursor), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateAsync(CreateUserRequest request, UserService users, CancellationToken cancellationToken) =>
        ApiResults.Created(
            await users.CreateAsync(
                new NewUser(request.UserName, request.Password) { DisplayName = request.DisplayName, Email = request.Email, IsAdmin = request.IsAdmin },
                cancellationToken).ConfigureAwait(false),
            user => $"/api/v1/users/{user.Id}");

    private static async Task<IResult> GetAsync(Guid id, UserService users, CancellationToken cancellationToken) =>
        ApiResults.Ok(await users.GetAsync(id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UpdateAsync(Guid id, UpdateUserRequest request, UserService users, CancellationToken cancellationToken) =>
        ApiResults.Ok(await users.UpdateAsync(
            id,
            new UserChanges { DisplayName = request.DisplayName, Email = request.Email, IsAdmin = request.IsAdmin, IsDisabled = request.IsDisabled },
            cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> DeleteAsync(Guid id, UserService users, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await users.DeleteAsync(id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ResetPasswordAsync(Guid id, ResetPasswordRequest request, UserService users, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await users.ResetPasswordAsync(id, request.Password, cancellationToken).ConfigureAwait(false));
}
