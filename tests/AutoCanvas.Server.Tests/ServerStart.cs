using AutoCanvas.Server.Hosting;
using AutoCanvas.TestSupport;

namespace AutoCanvas.Server.Tests;

/// <summary>
/// Starts the server the way <c>dotnet AutoCanvas.Server.dll</c> does, for tests of starts that must
/// fail. <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint"/> is no good
/// for those: when the start fails, the host is disposed while the factory still reads from it, so
/// the test sometimes sees an <see cref="ObjectDisposedException"/> instead of the real error.
/// </summary>
internal static class ServerStart
{
    /// <summary>
    /// The message of the <typeparamref name="TException"/> that stops the server. The database's
    /// settings and the test master key apply unless <paramref name="settings"/> change them; a
    /// <see langword="null"/> value removes a setting.
    /// </summary>
    public static async Task<string> FailsAsync<TException>(TestDatabase database, params (string Key, string? Value)[] settings)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(database);
        var all = new Dictionary<string, string?>(database.Settings, StringComparer.OrdinalIgnoreCase)
        {
            ["Secrets:MasterKey"] = AutoCanvasFactory.MasterKey,
            ["urls"] = "http://127.0.0.1:0",
            ["Logging:LogLevel:Default"] = "None",
        };
        foreach (var (key, value) in settings)
        {
            all[key] = value;
        }

        string[] args = [.. all.Where(s => s.Value is not null).Select(s => $"--{s.Key}={s.Value}")];
        var run = ServerCommandLine.RunAsync(args, TextWriter.Null, TextWriter.Null, TextReader.Null);
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        finished.ShouldBeSameAs(run, "The server started although it should have refused to.");
        return (await Should.ThrowAsync<TException>(run)).Message;
    }
}
