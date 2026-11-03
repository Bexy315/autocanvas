namespace AutoCanvas.Server.Api.Setup;

/// <summary>The master key the server generated, shown once so it can be kept apart from the database.</summary>
/// <param name="Key">The key (base64).</param>
/// <param name="File">Where the server keeps it.</param>
internal sealed record MasterKeyBackup(string Key, string File);
