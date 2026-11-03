namespace AutoCanvas.Server.Api.Setup;

/// <summary>Where the server's master key comes from.</summary>
internal enum MasterKeyOrigin
{
    /// <summary>The operator configured it; the setup does not show it.</summary>
    Configured,

    /// <summary>The server generated it on this start, into the data directory.</summary>
    Generated,

    /// <summary>The server uses the key it generated on an earlier start.</summary>
    DefaultFile,
}
