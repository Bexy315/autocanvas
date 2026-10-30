using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Identity;

/// <summary><c>/api/v1/me</c>: the signed-in user's own account and sessions.</summary>
internal static class MeEndpoints
{
    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Me");

        me.MapGet("/", GetMeAsync)
            .WithName("GetMe")
            .Produces<MeView>()
            .WithSummary("Get the signed-in user")
            .WithDescription("The account with its groups. Also works while the password must be changed, so clients can show that.")
            .RequireAuthorization(AutoCanvasPolicies.SignedIn);

        me.MapPut("/password", ChangePasswordAsync)
            .WithName("ChangePassword")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Change the password")
            .WithDescription("Needs the current password and a session (not an API token). Ends every other session of the user.")
            .RequireAuthorization(AutoCanvasPolicies.PasswordChange)
            .ProducesValidationProblem();

        me.MapGet("/sessions", ListSessionsAsync)
            .WithName("ListMySessions")
            .WithSummary("List my sessions")
            .WithDescription("Where the user is signed in, newest first; the current session is marked.")
            .RequireAuthorization(AutoCanvasPolicies.SessionOnly);

        me.MapDelete("/sessions/{id:guid}", EndSessionAsync)
            .WithName("EndMySession")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("End one of my sessions")
            .RequireAuthorization(AutoCanvasPolicies.SessionOnly)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/tokens", ListTokensAsync)
            .WithName("ListMyTokens")
            .Produces<IReadOnlyList<TokenView>>()
            .WithSummary("List my API tokens")
            .WithDescription("Newest first, without the secrets. Needs a session: a token cannot manage tokens.")
            .RequireAuthorization(AutoCanvasPolicies.SessionOnly);

        me.MapPost("/tokens", CreateTokenAsync)
            .WithName("CreateMyToken")
            .Produces<CreatedToken>(StatusCodes.Status201Created)
            .WithSummary("Create an API token")
            .WithDescription("The answer contains the secret once; store it right away. Send it as 'Authorization: Bearer acp_…'. Needs a session.")
            .RequireAuthorization(AutoCanvasPolicies.SessionOnly)
            .ProducesValidationProblem();

        me.MapDelete("/tokens/{id:guid}", DeleteTokenAsync)
            .WithName("DeleteMyToken")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Delete one of my API tokens")
            .WithDescription("Requests with the token fail from now on. Needs a session.")
            .RequireAuthorization(AutoCanvasPolicies.SessionOnly)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetMeAsync(HttpContext context, AccountService accounts, CancellationToken cancellationToken) =>
        ApiResults.Ok(await accounts.GetMeAsync(context.User.GetUserId(), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, HttpContext context, AccountService accounts, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await accounts.ChangePasswordAsync(
            context.User.GetUserId(),
            context.User.GetSessionId(),
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken).ConfigureAwait(false));

    private static async Task<IReadOnlyList<SessionView>> ListSessionsAsync(HttpContext context, SessionService sessions, CancellationToken cancellationToken) =>
        await sessions.ListAsync(context.User.GetUserId(), context.User.GetSessionId(), cancellationToken).ConfigureAwait(false);

    private static async Task<IResult> ListTokensAsync(HttpContext context, TokenService tokens, CancellationToken cancellationToken) =>
        ApiResults.Ok(await tokens.ListAsync(context.User.GetUserId(), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> CreateTokenAsync(CreateTokenRequest request, HttpContext context, TokenService tokens, CancellationToken cancellationToken) =>
        ApiResults.Created(
            await tokens.CreateAsync(context.User.GetUserId(), request.Name, request.ExpiresAt, cancellationToken).ConfigureAwait(false),
            token => $"/api/v1/me/tokens/{token.Details.Id}");

    private static async Task<IResult> DeleteTokenAsync(Guid id, HttpContext context, TokenService tokens, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await tokens.DeleteAsync(context.User.GetUserId(), id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> EndSessionAsync(Guid id, HttpContext context, SessionService sessions, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await sessions.EndAsync(context.User.GetUserId(), id, cancellationToken).ConfigureAwait(false));
}
