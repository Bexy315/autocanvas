namespace AutoCanvas.Core;

/// <summary>
/// Versions of the automation file format that this build understands.
/// </summary>
public static class AutomationSchemaVersion
{
    /// <summary>The newest automation file format version; new automations are written with it.</summary>
    public const int Current = 1;

    /// <summary>The oldest automation file format version that can still be read.</summary>
    public const int Oldest = 1;

    /// <summary>
    /// Returns whether an automation declaring <paramref name="version"/> can be read by this build.
    /// </summary>
    /// <param name="version">The value of the automation's <c>version</c> field.</param>
    /// <returns><see langword="true"/> if the version lies between <see cref="Oldest"/> and <see cref="Current"/>.</returns>
    public static bool IsSupported(int version) => version is >= Oldest and <= Current;
}
