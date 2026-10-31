using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Persistence.Runners;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using AutoCanvas.Server.Application.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Runners;

/// <summary>
/// How runners join the server (ADR-0017): an administrator creates a one-time registration token,
/// the runner exchanges it for its own credential. Both are 32 random bytes with a prefix, shown
/// once and stored as SHA-256 hashes like API tokens (ADR-0011).
/// </summary>
public sealed class RunnerRegistrationService(AutoCanvasDbContext database, IAuditLog audit, IAuditContext context, TimeProvider time)
{
    /// <summary>The prefix of every registration token.</summary>
    public const string TokenPrefix = "acrt_";

    /// <summary>The prefix of every runner credential; never accepted by the HTTP API.</summary>
    public const string CredentialPrefix = "acr_";

    /// <summary>How long a token lasts when the administrator does not say.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(1);

    /// <summary>The longest a token can last.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(30);

    /// <summary>Creates a registration token and returns its secret once.</summary>
    public async Task<UseCaseResult<CreatedRegistrationToken>> CreateTokenAsync(string? description, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description is { Length: > RunnerRegistrationToken.MaxDescriptionLength } || description?.Any(char.IsControl) == true)
        {
            return UseCase.Failure<CreatedRegistrationToken>(UseCaseError.Invalid("description", $"The description must have at most {RunnerRegistrationToken.MaxDescriptionLength} characters and no control characters."));
        }

        var expiry = expiresAt?.ToUniversalTime() ?? now + DefaultLifetime;
        if (expiry <= now || expiry > now + MaxLifetime)
        {
            return UseCase.Failure<CreatedRegistrationToken>(UseCaseError.Invalid("expiresAt", $"The expiry must be in the future and at most {MaxLifetime.Days} days away."));
        }

        var secret = SecretTokens.Create(TokenPrefix);
        var token = new RunnerRegistrationToken
        {
            Id = Guid.CreateVersion7(now),
            Description = description,
            TokenHash = SecretTokens.Hash(secret),
            Hint = $"{TokenPrefix}…{secret[^4..]}",
            CreatedAt = now,
            CreatedByName = Cut(context.Actor.Name, Runner.MaxCreatedByLength),
            ExpiresAt = expiry,
        };
        database.RunnerRegistrationTokens.Add(token);
        audit.Record(TokenEntry(AuditActions.RunnerTokenCreated, token.Id, ("description", token.Description), ("expiresAt", token.ExpiresAt)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new CreatedRegistrationToken(secret, View(token, now)));
    }

    /// <summary>All registration tokens, newest first, used and expired ones included.</summary>
    public async Task<IReadOnlyList<RegistrationTokenView>> ListTokensAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var tokens = await database.RunnerRegistrationTokens.AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. tokens.Select(t => View(t, now))];
    }

    /// <summary>Deletes a registration token; an unused one can no longer be used. Its runner stays.</summary>
    public async Task<UseCaseResult<Done>> DeleteTokenAsync(Guid id, CancellationToken cancellationToken)
    {
        var token = await database.RunnerRegistrationTokens.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound("The registration token does not exist."));
        }

        database.RunnerRegistrationTokens.Remove(token);
        audit.Record(TokenEntry(AuditActions.RunnerTokenDeleted, token.Id, ("description", token.Description)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }

    /// <summary>
    /// Registers a runner with a registration token, which is used up by it. Fails with
    /// <see cref="UseCaseErrorKind.Unauthorized"/> for an unknown or expired token and with a
    /// conflict for a used token or a taken name.
    /// </summary>
    public async Task<UseCaseResult<RegisteredRunner>> RegisterAsync(string registrationToken, string name, RunnerCapabilities capabilities, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(capabilities);
        var now = time.GetUtcNow();
        var found = await UsableTokenAsync(registrationToken, now, cancellationToken).ConfigureAwait(false);
        if ((found.Error ?? CheckName(name) ?? capabilities.Check()) is { } refused)
        {
            return UseCase.Failure<RegisteredRunner>(refused);
        }

        var token = found.Value!;
        name = name.Trim();
        var normalized = name.ToUpperInvariant();
        if (await database.Runners.AnyAsync(r => r.NormalizedName == normalized, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<RegisteredRunner>(NameTaken);
        }

        var credential = SecretTokens.Create(CredentialPrefix);
        var runner = new Runner
        {
            Id = Guid.CreateVersion7(now),
            Name = name,
            NormalizedName = normalized,
            CredentialHash = SecretTokens.Hash(credential),
            CredentialHint = $"{CredentialPrefix}…{credential[^4..]}",
            Capabilities = capabilities.Write(),
            CreatedAt = now,
            CreatedByName = token.CreatedByName,
            LastSeenAt = now,
            Version = 1,
        };
        database.Runners.Add(runner);

        // UsedAt is a concurrency token: of two registrations with the same token, one saves.
        token.UsedAt = now;
        token.RunnerId = runner.Id;
        audit.Record(new AuditEntry(AuditActions.RunnerRegistered)
        {
            TargetType = "runner",
            TargetId = runner.Id.ToString(),
            Actor = new AuditActor(AuditActorKind.Runner, runner.Id, runner.Name),
            Details = AuditEntry.DetailsOf(("name", runner.Name), ("tokenId", token.Id)),
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UseCase.Failure<RegisteredRunner>(UsedToken);
        }
        catch (DbUpdateException)
        {
            // Another runner took the name since the check above.
            return UseCase.Failure<RegisteredRunner>(NameTaken);
        }

        return UseCase.Success(new RegisteredRunner(runner.Id, credential));
    }

    private async Task<UseCaseResult<RunnerRegistrationToken>> UsableTokenAsync(string secret, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        RunnerRegistrationToken? token = null;
        if (secret.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            var hash = SecretTokens.Hash(secret);
            token = await database.RunnerRegistrationTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);
        }

        return token switch
        {
            { UsedAt: not null } => UseCase.Failure<RunnerRegistrationToken>(UsedToken),
            not null when token.ExpiresAt > now => UseCase.Success(token),
            _ => UseCase.Failure<RunnerRegistrationToken>(new UseCaseError(UseCaseErrorKind.Unauthorized, "The registration token is unknown or expired.")),
        };
    }

    /// <summary>An error for a runner name that is empty, too long or has control characters.</summary>
    internal static UseCaseError? CheckName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > Runner.MaxNameLength || name.Any(char.IsControl)
            ? UseCaseError.Invalid("name", $"The runner name must have 1 to {Runner.MaxNameLength} characters and no control characters.")
            : null;

    /// <summary>The error for a name another runner has.</summary>
    internal static UseCaseError NameTaken => UseCaseError.Conflict("Another runner has this name.", "name");

    private static UseCaseError UsedToken => UseCaseError.Conflict("The registration token has been used already.", "registrationToken");

    private static RegistrationTokenView View(RunnerRegistrationToken token, DateTimeOffset now) =>
        new(token.Id, token.Description, token.Hint, token.CreatedAt, token.CreatedByName, token.ExpiresAt, token.UsedAt, token.RunnerId, token.UsedAt is null && token.ExpiresAt > now);

    private static AuditEntry TokenEntry(string action, Guid tokenId, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "runner_token",
        TargetId = tokenId.ToString(),
        Details = AuditEntry.DetailsOf([("tokenId", tokenId), .. details]),
    };

    private static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
}
