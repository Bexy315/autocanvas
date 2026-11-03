namespace AutoCanvas.Server.Application.Setup;

/// <summary>Hands a registration token to the runner that came with the package; the host writes it to a file.</summary>
public interface IRunnerTokenHandoff
{
    /// <summary>Whether there is a bundled runner to hand a token to.</summary>
    bool IsAvailable { get; }

    /// <summary>Hands the token over; throws when it cannot.</summary>
    Task WriteAsync(string token, CancellationToken cancellationToken);
}
