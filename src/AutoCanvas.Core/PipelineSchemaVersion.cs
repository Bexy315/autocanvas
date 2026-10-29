namespace AutoCanvas.Core;

/// <summary>
/// Versions of the pipeline definition format that this build understands.
/// </summary>
public static class PipelineSchemaVersion
{
    /// <summary>The newest pipeline schema version; new pipelines are written with it.</summary>
    public const int Current = 1;

    /// <summary>The oldest pipeline schema version that can still be read.</summary>
    public const int Oldest = 1;

    /// <summary>
    /// Returns whether a pipeline declaring <paramref name="version"/> can be read by this build.
    /// </summary>
    /// <param name="version">The value of the pipeline's <c>version</c> field.</param>
    /// <returns><see langword="true"/> if the version lies between <see cref="Oldest"/> and <see cref="Current"/>.</returns>
    public static bool IsSupported(int version) => version is >= Oldest and <= Current;
}
