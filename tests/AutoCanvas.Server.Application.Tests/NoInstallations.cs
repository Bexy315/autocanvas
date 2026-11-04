using AutoCanvas.Forge;
using AutoCanvas.Server.Application.Repositories;

namespace AutoCanvas.Server.Application.Tests;

/// <summary>No GitHub App: tests that do not connect GitHub repositories.</summary>
internal sealed class NoInstallations : IInstallationTokens
{
    public static readonly NoInstallations Instance = new();

    public Task<string> TokenAsync(long installationId, CancellationToken cancellationToken) =>
        throw new ForgeException("No GitHub App in this test.");

    public Task<string> CheckoutTokenAsync(long installationId, long repositoryId, CancellationToken cancellationToken) =>
        throw new ForgeException("No GitHub App in this test.");

    public IReadOnlyList<string> Issued(long installationId) => [];
}
