using AutoCanvas.Server.Api.Triggers;
using AutoCanvas.Server.Application.Forges;
using AutoCanvas.Server.Application.Repositories;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Forges;

/// <summary>
/// The GitHub App (ADR-0022): <c>/api/v1/forges/github</c> for admins to set it up, the folder
/// endpoints to connect its repositories, and <c>POST /api/v1/forges/github/events</c>, which
/// GitHub calls with the app's events.
/// </summary>
internal static class GitHubEndpoints
{
    /// <summary>The rate limiting policy of the app's events: one address for every repository, so a limit of its own.</summary>
    public const string RateLimit = "github-events";

    /// <summary>How many events a minute the address takes.</summary>
    public const int EventsPerMinute = 600;

    public static void MapGitHubEndpoints(this RouteGroupBuilder api)
    {
        var github = api.MapGroup("/forges/github").WithTags("Forges").RequireAuthorization(AutoCanvasPolicies.Admin);

        github.MapGet("/", GetAsync)
            .WithName("GetGitHubApp")
            .Produces<GitHubForgeView>()
            .WithSummary("Get the GitHub App")
            .WithDescription("The instance's GitHub App without its secrets, with its installations and where GitHub sends its events; `app` is null while none is set up. Needs an admin.");

        github.MapPost("/manifest", ManifestAsync)
            .WithName("CreateGitHubAppManifest")
            .Produces<GitHubManifestView>()
            .WithSummary("Start creating the GitHub App")
            .WithDescription("The manifest of a new app and the GitHub address to post it to as the form field `manifest`, in the browser. GitHub then sends the browser back to `/admin/forges/github/created` with a code. Needs the instance's public address and an admin.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        github.MapPost("/manifest/launch", LaunchAsync)
            .WithName("LaunchGitHubAppManifest")
            .Produces<string>(StatusCodes.Status200OK, "text/html")
            .WithSummary("Take the browser to GitHub with the manifest")
            .WithDescription("A plain form post from the UI with the field `state` of a manifest of the last hour; answers a page that posts the manifest to GitHub at once. The state stands in for the X-AutoCanvas-Request header. Needs an admin.");

        github.MapPost("/created", CompleteAsync)
            .WithName("CompleteGitHubApp")
            .Produces<GitHubForgeView>()
            .WithSummary("Finish creating the GitHub App")
            .WithDescription("Turns the code GitHub handed the browser into the app, its private key and secrets, and stores them encrypted. Needs an admin.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        github.MapPut("/", ConfigureAsync)
            .WithName("ConfigureGitHubApp")
            .Produces<GitHubForgeView>()
            .WithSummary("Enter an existing GitHub App")
            .WithDescription("Enters an app created on GitHub by hand, or a new private key or webhook secret of the app; GitHub is asked whether the id and key belong together. Needs an admin.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        github.MapDelete("/", DeleteAsync)
            .WithName("DeleteGitHubApp")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("Remove the GitHub App")
            .WithDescription("Removes the app from the instance; it stays on GitHub. Refused while GitHub repositories are connected. Needs an admin.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        MapEvents(api);
        MapRepositories(api);
    }

    private static void MapEvents(RouteGroupBuilder api)
    {
        api.MapPost("/forges/github/events", ReceiveAsync)
            .WithTags("Forges")
            .WithName("ReceiveGitHubEvent")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimit)
            .Produces<ReceivedHookView>()
            .WithSummary("Take an event of the GitHub App")
            .WithDescription("GitHub calls it with the app's events, signed with its webhook secret (`X-Hub-Signature-256`); each delivery is taken once. A push starts its runs shortly; other events do nothing for now.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static void MapRepositories(RouteGroupBuilder api)
    {
        var inFolder = api.MapGroup("/folders/{id:guid}").WithTags("Repositories").RequireAuthorization();

        inFolder.MapGet("/github/repositories", ListRepositoriesAsync)
            .WithName("ListGitHubRepositories")
            .Produces<IReadOnlyList<GitHubRepositoryView>>()
            .WithSummary("List the repositories the GitHub App reaches")
            .WithDescription("The repositories of every installation of the app, to connect one in the folder. Needs Editor.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        inFolder.MapPost("/repositories/github", ConnectAsync)
            .WithName("ConnectGitHubRepository")
            .Produces<RepositoryView>(StatusCodes.Status201Created)
            .WithSummary("Connect a GitHub repository")
            .WithDescription("Connects a repository of an installation of the GitHub App in the folder and reads its automations; it is reached with the app's installation tokens. Needs Editor.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetAsync(GitHubAppService github, CancellationToken cancellationToken) =>
        TypedResults.Ok(await github.GetAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ManifestAsync(NewGitHubManifest? request, GitHubAppService github, CancellationToken cancellationToken) =>
        ApiResults.Ok(await github.ManifestAsync(request ?? new NewGitHubManifest(), cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> LaunchAsync(HttpContext context, GitHubAppService github, CancellationToken cancellationToken)
    {
        var state = context.Request.HasFormContentType ? (await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false))["state"].ToString() : string.Empty;
        var launched = await github.LaunchAsync(state, cancellationToken).ConfigureAwait(false);
        var (html, policy) = launched.Value is { } manifest ? GitHubLaunchPage.Launch(manifest) : GitHubLaunchPage.Refused(launched.Error!.Message);
        context.Response.Headers.ContentSecurityPolicy = policy;
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Content(html, "text/html; charset=utf-8", statusCode: launched.Value is null ? StatusCodes.Status400BadRequest : StatusCodes.Status200OK);
    }

    private static async Task<IResult> CompleteAsync(GitHubAppCompletion completion, HttpContext context, GitHubAppService github, CancellationToken cancellationToken) =>
        ApiResults.Ok(await github.CompleteAsync(context.User.ToActor(), completion, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ConfigureAsync(GitHubAppSettings settings, HttpContext context, GitHubAppService github, CancellationToken cancellationToken) =>
        ApiResults.Ok(await github.ConfigureAsync(context.User.ToActor(), settings, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> DeleteAsync(GitHubAppService github, CancellationToken cancellationToken) =>
        ApiResults.NoContent(await github.DeleteAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ListRepositoriesAsync(Guid id, HttpContext context, GitHubRepositoryService repositories, CancellationToken cancellationToken) =>
        ApiResults.Ok(await repositories.ListAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ConnectAsync(Guid id, NewGitHubRepository request, HttpContext context, GitHubRepositoryService repositories, CancellationToken cancellationToken) =>
        ApiResults.Created(await repositories.ConnectAsync(context.User.ToActor(), id, request, cancellationToken).ConfigureAwait(false), view => $"/api/v1/repositories/{view.Id}");

    private static async Task<IResult> ReceiveAsync(HttpContext context, GitHubEventService events, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var body = await WebhookEndpoints.ReadAsync(request.Body, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                type: ProblemTypes.TooLarge,
                title: "Too large",
                detail: "An event may have at most 1 MiB.");
        }

        var call = new RepositoryHookCall(body, request.Headers.Where(h => !WebhookEndpoints.IsCredential(h.Key)).ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase));
        return ApiResults.Ok(await events.ReceiveAsync(call, cancellationToken).ConfigureAwait(false));
    }
}
