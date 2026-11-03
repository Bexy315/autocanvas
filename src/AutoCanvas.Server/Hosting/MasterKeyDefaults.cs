using AutoCanvas.Server.Application.Variables;
using AutoCanvas.Server.Configuration;

namespace AutoCanvas.Server.Hosting;

/// <summary>
/// A master key for installations whose operator configured none (ADR-0020): <c>keys/master.key</c>
/// in the data directory, used when it exists and, when the server itself starts, created when it
/// does not. A key the operator configures always wins.
/// </summary>
internal static class MasterKeyDefaults
{
    public static void Apply(WebApplicationBuilder builder, bool generate)
    {
        var configuration = builder.Configuration;
        if (!string.IsNullOrWhiteSpace(configuration[$"{SecretsOptions.SectionName}:{nameof(SecretsOptions.MasterKey)}"])
            || !string.IsNullOrWhiteSpace(configuration[$"{SecretsOptions.SectionName}:{nameof(SecretsOptions.MasterKeyFile)}"]))
        {
            builder.Services.AddSingleton(MasterKeySource.Configured);
            return;
        }

        var dataDirectory = Path.GetFullPath(
            configuration[$"{ServerOptions.SectionName}:{nameof(ServerOptions.DataDirectory)}"] ?? ServerOptions.DefaultDataDirectory,
            builder.Environment.ContentRootPath);
        var file = Path.Combine(dataDirectory, "keys", "master.key");
        var generated = false;
        if (!File.Exists(file))
        {
            if (!generate)
            {
                // The options then report that no key is configured.
                builder.Services.AddSingleton(MasterKeySource.Configured);
                return;
            }

            Write(file, MasterKey.Generate());
            generated = true;
        }

        configuration.AddInMemoryCollection([new($"{SecretsOptions.SectionName}:{nameof(SecretsOptions.MasterKeyFile)}", file)]);
        builder.Services.AddSingleton(new MasterKeySource(file, generated));
    }

    /// <summary>Writes the key readable by the server's user only.</summary>
    private static void Write(string file, MasterKey key)
    {
        var directory = Path.GetDirectoryName(file)!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        using var stream = PrivateFile.Create(file, FileMode.CreateNew);
        using var writer = new StreamWriter(stream);
        writer.Write(key.Text);
        writer.Write('\n');
    }
}
