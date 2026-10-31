using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Variables;
using AutoCanvas.Server.Authentication;

namespace AutoCanvas.Server.Api.Variables;

/// <summary>
/// Variables and secrets (ADR-0013) of the instance (<c>/api/v1/variables</c>, <c>/secrets</c>),
/// of folders (<c>/folders/{id}/…</c>) and secrets of automations (<c>/automations/{id}/secrets</c>),
/// plus where a folder's or automation's values come from (<c>…/configuration</c>). Secret values
/// go in and never come out.
/// </summary>
internal static class VariableEndpoints
{
    public static void MapVariableEndpoints(this RouteGroupBuilder api)
    {
        var instance = api.MapGroup(string.Empty).WithTags("Variables and secrets").RequireAuthorization();
        MapVariables(instance, "/variables", "Instance", "instance", _ => ConfigurationScope.Instance, "everyone signed in reads them with their values; only admins change them");
        MapSecrets(instance, "/secrets", "Instance", "instance", _ => ConfigurationScope.Instance, "everyone signed in sees their names; only admins change them");

        var folders = api.MapGroup("/folders/{id:guid}").WithTags("Variables and secrets").RequireAuthorization();
        MapVariables(folders, "/variables", "Folder", "folder", ConfigurationScope.Folder, "Viewers see names, Operators also values, Editors change them");
        MapSecrets(folders, "/secrets", "Folder", "folder", ConfigurationScope.Folder, "Viewers see names; Editors change them");
        folders.MapGet("/configuration", ExplainFolderAsync)
            .WithName("ExplainFolderConfiguration")
            .Produces<ConfigurationView>()
            .WithSummary("Explain a folder's variables and secrets")
            .WithDescription("Every name that reaches the folder, each with all its definitions from the instance down and the one that wins. Variable values for Operators; never secret values. Needs Viewer.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        var automations = api.MapGroup("/automations/{id:guid}").WithTags("Variables and secrets").RequireAuthorization();
        MapSecrets(automations, "/secrets", "Automation", "automation", ConfigurationScope.Automation, "roles of its folder: Viewers see names, Editors change them");
        automations.MapGet("/configuration", ExplainAutomationAsync)
            .WithName("ExplainAutomationConfiguration")
            .Produces<ConfigurationView>()
            .WithSummary("Explain an automation's variables and secrets")
            .WithDescription("Every name a run of the automation reads, each with all its definitions (instance, folders, the automation's secrets and the variables in its file) and the one that wins. Variable values for Operators; never secret values. Needs Viewer.")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapVariables(RouteGroupBuilder group, string path, string scope, string noun, Func<Guid, ConfigurationScope> scopeOf, string access)
    {
        group.MapGet(path, async (HttpRequest request, HttpContext context, VariableService variables, CancellationToken cancellationToken) =>
                ApiResults.Ok(await variables.ListAsync(context.User.ToActor(), scopeOf(RouteId(request)), cancellationToken).ConfigureAwait(false)))
            .WithName($"List{scope}Variables")
            .Produces<IReadOnlyList<VariableView>>()
            .WithSummary($"List {noun} variables")
            .WithDescription($"By name; {access}.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut(path + "/{name}", async (string name, SetValueRequest body, HttpRequest request, HttpContext context, VariableService variables, CancellationToken cancellationToken) =>
                Stored(await variables.SetAsync(context.User.ToActor(), scopeOf(RouteId(request)), name, body.Value, cancellationToken).ConfigureAwait(false), request))
            .WithName($"Set{scope}Variable")
            .Produces<VariableView>()
            .Produces<VariableView>(StatusCodes.Status201Created)
            .WithSummary($"Set a {noun} variable")
            .WithDescription($"Creates it (201) or replaces its value (200); names are unique regardless of case. {Capitalized(access)}.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete(path + "/{name}", async (string name, HttpRequest request, HttpContext context, VariableService variables, CancellationToken cancellationToken) =>
                ApiResults.NoContent(await variables.DeleteAsync(context.User.ToActor(), scopeOf(RouteId(request)), name, cancellationToken).ConfigureAwait(false)))
            .WithName($"Delete{scope}Variable")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary($"Delete a {noun} variable")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static void MapSecrets(RouteGroupBuilder group, string path, string scope, string noun, Func<Guid, ConfigurationScope> scopeOf, string access)
    {
        group.MapGet(path, async (HttpRequest request, HttpContext context, SecretService secrets, CancellationToken cancellationToken) =>
                ApiResults.Ok(await secrets.ListAsync(context.User.ToActor(), scopeOf(RouteId(request)), cancellationToken).ConfigureAwait(false)))
            .WithName($"List{scope}Secrets")
            .Produces<IReadOnlyList<SecretView>>()
            .WithSummary($"List {noun} secrets")
            .WithDescription($"Names only, by name; {access}. Secret values are never returned.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut(path + "/{name}", async (string name, SetValueRequest body, HttpRequest request, HttpContext context, SecretService secrets, CancellationToken cancellationToken) =>
                Stored(await secrets.SetAsync(context.User.ToActor(), scopeOf(RouteId(request)), name, body.Value, cancellationToken).ConfigureAwait(false), request))
            .WithName($"Set{scope}Secret")
            .Produces<SecretView>()
            .Produces<SecretView>(StatusCodes.Status201Created)
            .WithSummary($"Set a {noun} secret")
            .WithDescription($"Creates it (201) or replaces its value (200). The value is encrypted and cannot be read back. {Capitalized(access)}.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete(path + "/{name}", async (string name, HttpRequest request, HttpContext context, SecretService secrets, CancellationToken cancellationToken) =>
                ApiResults.NoContent(await secrets.DeleteAsync(context.User.ToActor(), scopeOf(RouteId(request)), name, cancellationToken).ConfigureAwait(false)))
            .WithName($"Delete{scope}Secret")
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary($"Delete a {noun} secret")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ExplainFolderAsync(Guid id, HttpContext context, ConfigurationService configuration, CancellationToken cancellationToken) =>
        ApiResults.Ok(await configuration.ExplainFolderAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> ExplainAutomationAsync(Guid id, HttpContext context, ConfigurationService configuration, CancellationToken cancellationToken) =>
        ApiResults.Ok(await configuration.ExplainAutomationAsync(context.User.ToActor(), id, cancellationToken).ConfigureAwait(false));

    /// <summary>201 with the new item's address when the name was new, else 200.</summary>
    private static IResult Stored<T>(UseCaseResult<Stored<T>> result, HttpRequest request)
    {
        if (result.Error is { } error)
        {
            return ApiResults.Problem(error);
        }

        var stored = result.Value!;
        return stored.Created
            ? TypedResults.Created(new Uri(request.Path.Value!, UriKind.Relative), stored.Item)
            : TypedResults.Ok(stored.Item);
    }

    /// <summary>The folder or automation id of the route; the instance routes have none.</summary>
    private static Guid RouteId(HttpRequest request) =>
        request.RouteValues.TryGetValue("id", out var id) && Guid.TryParse(id?.ToString(), out var guid) ? guid : Guid.Empty;

    private static string Capitalized(string text) => string.Concat(text[..1].ToUpperInvariant(), text.AsSpan(1));
}
