using AutoCanvas.Server.Api.Identity;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using AutoCanvas.Server.Application.Instance;
using AutoCanvas.Server.Application.Setup;
using AutoCanvas.Server.Hosting;
using AutoCanvas.Server.Setup;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AutoCanvas.Server.Api.Setup;

/// <summary>
/// <c>/api/v1/setup</c>: setting up a fresh installation in the browser, with the token from the
/// server's log (ADR-0020). The only API that answers while setup mode is on.
/// </summary>
internal static class SetupEndpoints
{
    public const string RateLimit = "setup";

    public const string TokenHeader = "X-AutoCanvas-Setup-Token";

    public static void MapSetupEndpoints(this RouteGroupBuilder api)
    {
        var setup = api.MapGroup("/setup").WithTags("Setup").AllowAnonymous().RequireRateLimiting(RateLimit);

        setup.MapGet("/", GetAsync)
            .WithName("GetSetup")
            .Produces<SetupStatus>()
            .WithSummary("Whether setup is needed")
            .WithDescription($"`open` is true while the installation has no account. With the setup token from the server's log in `{TokenHeader}`, also what the setup asks about. A wrong token is refused with 401; too many attempts from one address with 429.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        setup.MapPost("/", CompleteAsync)
            .WithName("CompleteSetup")
            .Produces<SetupCompleted>()
            .WithSummary("Set the installation up")
            .WithDescription($"Creates the first admin and stores the addresses in one go, optionally with the example automation and a registration token for the bundled runner, and signs the admin in. Needs the setup token in `{TokenHeader}`. Afterwards setup mode is over for good: this answers 404.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> GetAsync(
        HttpContext context,
        SetupState state,
        InstanceSettingsService instance,
        SetupService setup,
        IRunnerTokenHandoff handoff,
        MasterKeySource key,
        CancellationToken cancellationToken)
    {
        if (!state.IsOpen || !await setup.IsOpenAsync(cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.Ok(new SetupStatus(false, null));
        }

        var token = context.Request.Headers[TokenHeader].ToString();
        if (token.Length == 0)
        {
            return TypedResults.Ok(new SetupStatus(true, null));
        }

        if (!state.Matches(token))
        {
            return WrongToken();
        }

        var settings = await instance.GetAsync(cancellationToken).ConfigureAwait(false);
        var origin = key.DefaultFile is null ? MasterKeyOrigin.Configured : key.GeneratedNow ? MasterKeyOrigin.Generated : MasterKeyOrigin.DefaultFile;
        return TypedResults.Ok(new SetupStatus(true, new SetupDetails(settings.PublicAddress, settings.RunnerAddress, handoff.IsAvailable, origin)));
    }

    private static async Task<IResult> CompleteAsync(
        SetupRequest request,
        HttpContext context,
        SetupState state,
        SetupService setup,
        SignInService signIn,
        InstanceSettingsService instance,
        MasterKeySource key,
        CancellationToken cancellationToken)
    {
        if (!state.IsOpen)
        {
            return ApiResults.Problem(UseCaseError.NotFound("This installation is set up already."));
        }

        if (!state.Matches(context.Request.Headers[TokenHeader].ToString()))
        {
            return WrongToken();
        }

        await state.Completing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!state.IsOpen)
            {
                return ApiResults.Problem(UseCaseError.NotFound("This installation is set up already."));
            }

            var result = await setup.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Error is { } error)
            {
                return ApiResults.Problem(error);
            }

            state.Close();
            var signedIn = await signIn.SignInAsync(
                request.Admin.UserName,
                request.Admin.Password,
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString(),
                cancellationToken).ConfigureAwait(false);
            await AuthEndpoints.SetSessionCookieAsync(context, instance, signedIn.Value!, cancellationToken).ConfigureAwait(false);

            var backup = key.DefaultFile is { } file
                ? new MasterKeyBackup((await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)).Trim(), file)
                : null;
            var outcome = result.Value!;
            return TypedResults.Ok(new SetupCompleted(signedIn.Value!.Me, backup, outcome.RunnerTokenWritten, outcome.ExampleAutomationId));
        }
        finally
        {
            state.Completing.Release();
        }
    }

    private static ProblemHttpResult WrongToken() => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        type: ProblemTypes.Unauthorized,
        title: "Unauthorized",
        detail: "The setup token is not this server's. Copy the setup address from the server's log again; it changes when the server restarts.");
}
