using System.Globalization;

namespace AutoCanvas.Server.Application.Folders;

/// <summary>The materialized path of a folder: <c>/id/id/</c> from the top level down.</summary>
internal static class FolderPaths
{
    public static string Child(string? parentPath, Guid id) =>
        string.Create(CultureInfo.InvariantCulture, $"{parentPath ?? "/"}{id}/");

    public static IReadOnlyList<Guid> Ids(string path) =>
        [.. path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(id => Guid.Parse(id, CultureInfo.InvariantCulture))];

    public static int Depth(string path) => path.Count(c => c == '/') - 1;
}
