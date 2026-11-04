namespace AutoCanvas.Forge;

/// <summary>Where a repository keeps its automations (ADR-0003), and how much of them AutoCanvas reads.</summary>
public static class AutomationPaths
{
    /// <summary>The directory, from the repository's root.</summary>
    public const string Directory = ".autocanvas/automations";

    /// <summary>The largest automation file read.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>The most automation files one repository may have.</summary>
    public const int MaxFiles = 100;

    /// <summary>Whether a path is an automation file: directly in <see cref="Directory"/>, ending in <c>.yml</c> or <c>.yaml</c>.</summary>
    public static bool IsAutomation(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.StartsWith(Directory + "/", StringComparison.Ordinal)
            && path.IndexOf('/', Directory.Length + 1) < 0
            && (path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal));
    }
}
