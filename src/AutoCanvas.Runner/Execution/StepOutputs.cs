using System.Text;

namespace AutoCanvas.Runner.Execution;

/// <summary>
/// Reads the file a script step names in <c>AUTOCANVAS_OUTPUT</c>: one <c>name=value</c> per line,
/// split at the first <c>=</c>; a name written twice keeps its last value.
/// </summary>
internal static class StepOutputs
{
    /// <summary>The most of the file that is read; outputs are small values, not files.</summary>
    public const int MaxBytes = 1024 * 1024;

    public static async Task<Dictionary<string, string>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new(StringComparer.Ordinal);
        }

        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[(int)Math.Min(stream.Length, MaxBytes)];
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            return Parse(Encoding.UTF8.GetString(buffer));
        }
    }

    public static Dictionary<string, string> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            var name = equals > 0 ? line[..equals].Trim() : string.Empty;
            if (name.Length > 0)
            {
                outputs[name] = line[(equals + 1)..].TrimEnd('\r');
            }
        }

        return outputs;
    }
}
