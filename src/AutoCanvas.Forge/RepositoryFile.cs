namespace AutoCanvas.Forge;

/// <summary>An automation file in a repository.</summary>
/// <param name="Path">Its path from the repository's root, with <c>/</c>, e.g. <c>.autocanvas/automations/build.yml</c>.</param>
/// <param name="Content">Its text.</param>
public sealed record RepositoryFile(string Path, string Content)
{
    /// <summary>The automation's name: the file name without its extension.</summary>
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}
