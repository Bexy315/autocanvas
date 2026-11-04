namespace AutoCanvas.Server.Application.Repositories;

/// <summary>Installation tokens of the instance's GitHub App (ADR-0022), the credential of GitHub repositories.</summary>
public interface IInstallationTokens
{
    /// <summary>A token for the installation that is valid for a while yet; throws <see cref="Forge.ForgeException"/> when there is none to be had.</summary>
    Task<string> TokenAsync(long installationId, CancellationToken cancellationToken);

    /// <summary>The tokens handed out for the installation lately, to mask them in logs.</summary>
    IReadOnlyList<string> Issued(long installationId);
}
