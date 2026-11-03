using System.Security.Cryptography;
using System.Text;

namespace AutoCanvas.Server.Setup;

/// <summary>
/// Whether this server is in setup mode, and the one-time token that proves the caller can read the
/// server's log (ADR-0020). The token lives in memory only: a restart makes a new one.
/// </summary>
internal sealed class SetupState
{
    private readonly byte[] token = Encoding.ASCII.GetBytes(Base64Url(RandomNumberGenerator.GetBytes(32)));
    private volatile bool open;

    /// <summary>Lets one setup finish at a time, so two quick callers cannot both create an admin.</summary>
    public SemaphoreSlim Completing { get; } = new(1, 1);

    /// <summary>Whether setup mode is on.</summary>
    public bool IsOpen => open;

    /// <summary>The token, for the log line.</summary>
    public string Token => Encoding.ASCII.GetString(token);

    public void Open() => open = true;

    public void Close() => open = false;

    /// <summary>Whether the given token is this server's, compared in constant time.</summary>
    public bool Matches(string? candidate) =>
        candidate is not null && CryptographicOperations.FixedTimeEquals(token, Encoding.ASCII.GetBytes(candidate));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
