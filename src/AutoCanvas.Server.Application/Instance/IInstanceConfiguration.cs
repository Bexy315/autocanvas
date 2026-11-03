namespace AutoCanvas.Server.Application.Instance;

/// <summary>
/// The instance settings the server's configuration sets; the host provides them. A value set here
/// wins over the one stored in the database.
/// </summary>
public interface IInstanceConfiguration
{
    /// <summary><c>Server:PublicUrl</c>, if set.</summary>
    Uri? PublicUrl { get; }

    /// <summary><c>Runners:PublicUrl</c>, if set.</summary>
    Uri? RunnerUrl { get; }
}
