namespace AutoCanvas.Server.Hosting;

/// <summary>Where the master key came from, for the setup, which shows a key it did not get from the operator.</summary>
/// <param name="DefaultFile">The key file in the data directory, when the key is not configured; otherwise <see langword="null"/>.</param>
/// <param name="GeneratedNow">Whether this start created that file.</param>
internal sealed record MasterKeySource(string? DefaultFile, bool GeneratedNow)
{
    public static MasterKeySource Configured { get; } = new(null, false);
}
