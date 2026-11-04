using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AutoCanvas.Forge;

/// <summary>
/// Runs the <c>git</c> command line for a forge. The credential reaches git as an HTTP header
/// through <c>GIT_CONFIG_*</c> variables, as in the runner: never as an argument (visible in the
/// process list) and never in a config file. Only <c>http</c> and <c>https</c> are allowed, also
/// after redirects, and git never asks for input.
/// </summary>
internal sealed class GitCli(TimeSpan timeout)
{
    /// <summary>Runs git and returns what it wrote to standard output; throws <see cref="ForgeException"/> when it fails.</summary>
    public async Task<string> RunAsync(IReadOnlyList<string> arguments, RepositoryAccess? access, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in Environment(access))
        {
            start.Environment[key] = value;
        }

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new ForgeException("The git command line is not installed on the server.", exception);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new ForgeException($"git did not answer within {timeout.TotalSeconds:0} seconds.", exception);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new ForgeException(Describe(await error.ConfigureAwait(false), access));
        }

        return await output.ConfigureAwait(false);
    }

    /// <summary>The environment of a git call: no prompts, no system config, only http(s), and the credential as a header.</summary>
    internal static Dictionary<string, string> Environment(RepositoryAccess? access)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_ASKPASS"] = string.Empty,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_ALLOW_PROTOCOL"] = "http:https",
            ["LC_ALL"] = "C",
        };
        if (access?.Secret is { Length: > 0 })
        {
            environment["GIT_CONFIG_COUNT"] = "1";
            environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            environment["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {Basic(access)}";
        }

        return environment;
    }

    /// <summary>What git said, in one line and without the credential, should git ever echo it.</summary>
    internal static string Describe(string error, RepositoryAccess? access)
    {
        var text = string.Join(' ', error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (access?.Secret is { Length: > 0 } secret)
        {
            text = text.Replace(Basic(access), "***", StringComparison.Ordinal).Replace(secret, "***", StringComparison.Ordinal);
        }

        return text.Length == 0 ? "git failed without saying why." : $"git: {text}";
    }

    private static string Basic(RepositoryAccess access) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{(string.IsNullOrEmpty(access.UserName) ? RepositoryAccess.TokenUserName : access.UserName)}:{access.Secret}"));
}
