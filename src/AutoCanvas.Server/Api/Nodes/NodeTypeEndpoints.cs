using AutoCanvas.Core.Nodes;

namespace AutoCanvas.Server.Api.Nodes;

/// <summary><c>/api/v1/node-types</c>: the node types automations may use (ADR-0016).</summary>
internal static class NodeTypeEndpoints
{
    /// <summary>The catalog never changes while the server runs.</summary>
    private static readonly IReadOnlyList<NodeTypeView> Catalog =
        [.. BuiltInNodeTypes.All.Values.OrderBy(t => t.Name, StringComparer.Ordinal).Select(NodeTypeView.From)];

    public static void MapNodeTypeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/node-types", () => Catalog)
            .WithName("ListNodeTypes")
            .WithTags("Automations")
            .WithSummary("List the node types")
            .WithDescription("Every node type a step can use with `type:`, with the inputs it takes under `with` and the outputs it sets. Files using any other type are refused.")
            .RequireAuthorization();
    }
}
