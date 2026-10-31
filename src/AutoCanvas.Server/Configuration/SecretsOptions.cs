using AutoCanvas.Server.Application.Variables;

namespace AutoCanvas.Server.Configuration;

/// <summary>
/// Where the master key comes from (configuration section <c>Secrets</c>, ADR-0013): the key itself
/// or a file holding it, never both. The server does not start without one.
/// </summary>
internal sealed class SecretsOptions
{
    public const string SectionName = "Secrets";

    /// <summary>The master key: 32 bytes, base64-encoded (<c>admin generate-master-key</c> makes one).</summary>
    public string? MasterKey { get; set; }

    /// <summary>A file whose content is the master key, e.g. a container secret.</summary>
    public string? MasterKeyFile { get; set; }

    /// <summary>The configured key, or why there is none.</summary>
    public MasterKey? Load(out string? problem)
    {
        var hasKey = !string.IsNullOrWhiteSpace(MasterKey);
        var hasFile = !string.IsNullOrWhiteSpace(MasterKeyFile);
        if (hasKey == hasFile)
        {
            problem = hasKey
                ? "Set either Secrets:MasterKey (AUTOCANVAS_MASTER_KEY) or Secrets:MasterKeyFile (AUTOCANVAS_MASTER_KEY_FILE), not both."
                : "No master key is configured. Set AUTOCANVAS_MASTER_KEY or AUTOCANVAS_MASTER_KEY_FILE; 'admin generate-master-key' creates one. Back it up: without it, secrets cannot be recovered.";
            return null;
        }

        string text;
        if (hasFile)
        {
            try
            {
                text = File.ReadAllText(MasterKeyFile!);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problem = $"The master key file '{MasterKeyFile}' cannot be read: {exception.Message}";
                return null;
            }
        }
        else
        {
            text = MasterKey!;
        }

        return Application.Variables.MasterKey.Parse(text, out problem);
    }
}
