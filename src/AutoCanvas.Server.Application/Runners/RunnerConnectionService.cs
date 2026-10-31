using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Runners;

/// <summary>
/// What a runner's connection asks of the database besides the queue: who a credential belongs
/// to, the capabilities the runner announces, and whether it still exists and may take jobs.
/// </summary>
public sealed class RunnerConnectionService(AutoCanvasDbContext database, TimeProvider time)
{
    /// <summary>The runner a credential belongs to; <see langword="null"/> if it is unknown or the runner was deleted.</summary>
    public async Task<RunnerState?> AuthenticateAsync(string credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!credential.StartsWith(RunnerRegistrationService.CredentialPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = SecretTokens.Hash(credential);
        return await database.Runners.AsNoTracking()
            .Where(r => r.CredentialHash == hash)
            .Select(r => new RunnerState(r.Id, r.Name, r.IsPaused))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stores the capabilities a runner announces when it connects; they replace the earlier ones.</summary>
    /// <returns>The runner, or <see cref="UseCaseErrorKind.NotFound"/> if it was deleted.</returns>
    public async Task<UseCaseResult<RunnerState>> ConnectAsync(Guid runnerId, RunnerCapabilities capabilities, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (capabilities.Check() is { } invalid)
        {
            return UseCase.Failure<RunnerState>(invalid);
        }

        var now = time.GetUtcNow();
        var json = capabilities.Write();
        await database.Runners.Where(r => r.Id == runnerId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Capabilities, json).SetProperty(r => r.LastSeenAt, now), cancellationToken)
            .ConfigureAwait(false);
        return await StateAsync(runnerId, cancellationToken).ConfigureAwait(false) is { } state
            ? UseCase.Success(state)
            : UseCase.Failure<RunnerState>(UseCaseError.NotFound("The runner was deleted."));
    }

    /// <summary>Notes that the runner is alive; <see langword="null"/> once it was deleted.</summary>
    public async Task<RunnerState?> HeartbeatAsync(Guid runnerId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await database.Runners.Where(r => r.Id == runnerId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.LastSeenAt, now), cancellationToken)
            .ConfigureAwait(false);
        return await StateAsync(runnerId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The runner as it is now; <see langword="null"/> once it was deleted.</summary>
    public Task<RunnerState?> StateAsync(Guid runnerId, CancellationToken cancellationToken) =>
        database.Runners.AsNoTracking()
            .Where(r => r.Id == runnerId)
            .Select(r => new RunnerState(r.Id, r.Name, r.IsPaused))
            .FirstOrDefaultAsync(cancellationToken);
}
