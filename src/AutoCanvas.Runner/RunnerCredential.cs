using System.Text.Json;

namespace AutoCanvas.Runner;

/// <summary>
/// What registration gave the runner: its id and the credential it connects with. Kept in a file
/// only the runner's user may read; the credential is never logged.
/// </summary>
/// <param name="RunnerId">The runner's id on the server.</param>
/// <param name="Credential">The credential (<c>acr_…</c>).</param>
internal sealed record RunnerCredential(Guid RunnerId, string Credential)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The credential in the file, or <see langword="null"/> if there is no file.</summary>
    public static async Task<RunnerCredential?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            var credential = await JsonSerializer.DeserializeAsync<RunnerCredential>(stream, Json, cancellationToken).ConfigureAwait(false);
            return credential is { RunnerId: var id, Credential: { Length: > 0 } } && id != Guid.Empty
                ? credential
                : throw new InvalidDataException($"{path} is not a runner credential file; register the runner again.");
        }
    }

    /// <summary>Writes the file, readable by the owner only where the platform supports file modes.</summary>
    public async Task WriteAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        var stream = new FileStream(path, options);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, this, Json, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Never shows the credential, should the record end up in a log.</summary>
    public override string ToString() => $"RunnerCredential {{ RunnerId = {RunnerId} }}";
}
