using AutoCanvas.Server.Application.Identity;

namespace AutoCanvas.Server.Api.Identity;

/// <summary><c>/api/v1/directory</c>: finding people and groups by name, to share folders with them.</summary>
internal static class DirectoryEndpoints
{
    public static void MapDirectoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/directory", SearchAsync)
            .WithTags("Users")
            .RequireAuthorization()
            .WithName("SearchDirectory")
            .Produces<DirectoryView>()
            .WithSummary("Find people and groups")
            .WithDescription($"Users (not disabled) whose user name or display name contains `query`, and groups whose name does, ignoring case; names that start with it first, at most {DirectoryService.MaxResults} of each. Needs at least {DirectoryService.MinQueryLength} characters. Names only, never e-mail addresses. Everyone signed in may search, to share folders.")
            .ProducesValidationProblem();
    }

    private static async Task<IResult> SearchAsync(string? query, DirectoryService directory, CancellationToken cancellationToken) =>
        ApiResults.Ok(await directory.SearchAsync(query, cancellationToken).ConfigureAwait(false));
}
