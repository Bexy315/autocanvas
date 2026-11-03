using AutoCanvas.Server.Application.Instance;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Instance;

/// <summary><c>/api/v1/instance</c>: the installation's addresses, for admins (ADR-0020).</summary>
internal static class InstanceEndpoints
{
    public static void MapInstanceEndpoints(this RouteGroupBuilder api)
    {
        var instance = api.MapGroup("/instance").WithTags("Server").RequireAuthorization(AutoCanvasPolicies.Admin);

        instance.MapGet("/", GetAsync)
            .WithName("GetInstanceSettings")
            .Produces<InstanceSettingsView>()
            .WithSummary("Read the installation's addresses")
            .WithDescription("The address people reach the server at and the one runners reach it at, each with where it comes from: `configuration` (set in the server's settings, which wins and cannot be changed here), `database` or `none`.");

        instance.MapPut("/", UpdateAsync)
            .WithName("UpdateInstanceSettings")
            .Produces<InstanceSettingsView>()
            .WithSummary("Change the installation's addresses")
            .WithDescription("Sets both addresses: absolute http or https, without a path; empty clears one. An address the configuration sets must be sent empty or unchanged (409 otherwise).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetAsync(InstanceSettingsService instance, CancellationToken cancellationToken) =>
        TypedResults.Ok(await instance.GetAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> UpdateAsync(InstanceSettingsChange change, HttpContext context, InstanceSettingsService instance, CancellationToken cancellationToken) =>
        ApiResults.Ok(await instance.UpdateAsync(context.User.GetUserId(), change.PublicAddress, change.RunnerAddress, cancellationToken).ConfigureAwait(false));
}
