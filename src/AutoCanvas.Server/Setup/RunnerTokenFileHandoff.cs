using AutoCanvas.Server.Application.Setup;
using AutoCanvas.Server.Hosting;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Setup;

/// <summary>
/// Hands a registration token to the bundled runner by writing it to <c>Setup:RunnerTokenFile</c>,
/// on a volume the runner reads (ADR-0020). Readable by the server's user and group only.
/// </summary>
internal sealed class RunnerTokenFileHandoff(IOptions<SetupOptions> options) : IRunnerTokenHandoff
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.RunnerTokenFile);

    public async Task WriteAsync(string token, CancellationToken cancellationToken)
    {
        var file = Path.GetFullPath(options.Value.RunnerTokenFile!);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        // Written beside it and moved, so the runner never reads half a token.
        var partial = file + ".partial";
        var stream = PrivateFile.Create(partial, FileMode.Create, groupRead: true);
        await using (stream.ConfigureAwait(false))
        {
            var writer = new StreamWriter(stream);
            await using (writer.ConfigureAwait(false))
            {
                await writer.WriteAsync(token.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(partial, file, overwrite: true);
    }
}
