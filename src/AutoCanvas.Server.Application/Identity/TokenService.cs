using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Identity;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>
/// Personal API tokens for scripts and tools (ADR-0011): <c>acp_</c> plus 32 random bytes, shown
/// once, stored as a SHA-256 hash, optionally expiring. A token acts with its owner's current
/// permissions, so disabling the owner stops it.
/// </summary>
public sealed class TokenService(AutoCanvasDbContext database, IAuditLog audit, TimeProvider time)
{
    /// <summary>The prefix of every token; lets secret scanners and the server recognize them.</summary>
    public const string Prefix = "acp_";

    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    /// <summary>Creates a token for the user and returns its secret once.</summary>
    public async Task<UseCaseResult<CreatedToken>> CreateAsync(Guid userId, string name, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > ApiToken.MaxNameLength || name.Any(char.IsControl))
        {
            return UseCase.Failure<CreatedToken>(UseCaseError.Invalid("name", $"The token name must have 1 to {ApiToken.MaxNameLength} characters and no control characters."));
        }

        var now = time.GetUtcNow();
        if (expiresAt is { } expiry && expiry <= now)
        {
            return UseCase.Failure<CreatedToken>(UseCaseError.Invalid("expiresAt", "The expiry must be in the future."));
        }

        if (!await database.Users.AnyAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<CreatedToken>(UserNotFound);
        }

        var secret = SecretTokens.Create(Prefix);
        var token = new ApiToken
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Name = name.Trim(),
            TokenHash = SecretTokens.Hash(secret),
            Hint = $"{Prefix}…{secret[^4..]}",
            CreatedAt = now,
            ExpiresAt = expiresAt?.ToUniversalTime(),
        };
        database.ApiTokens.Add(token);
        audit.Record(Entry(AuditActions.TokenCreated, userId, ("tokenId", token.Id), ("name", token.Name), ("expiresAt", token.ExpiresAt)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new CreatedToken(secret, View(token, now)));
    }

    /// <summary>The user's tokens, newest first.</summary>
    public async Task<UseCaseResult<IReadOnlyList<TokenView>>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await database.Users.AnyAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<IReadOnlyList<TokenView>>(UserNotFound);
        }

        var now = time.GetUtcNow();
        var tokens = await database.ApiTokens.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success<IReadOnlyList<TokenView>>([.. tokens.Select(t => View(t, now))]);
    }

    /// <summary>Deletes one of the user's tokens; requests with it fail from now on.</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Guid userId, Guid tokenId, CancellationToken cancellationToken)
    {
        var token = await database.ApiTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound("The token does not exist."));
        }

        database.ApiTokens.Remove(token);
        audit.Record(Entry(AuditActions.TokenDeleted, userId, ("tokenId", token.Id), ("name", token.Name)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }

    /// <summary>
    /// The owner of a token, or <see langword="null"/> when the token is unknown, expired or its
    /// owner disabled.
    /// </summary>
    public async Task<SignedInUser?> ValidateAsync(string secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (!secret.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = SecretTokens.Hash(secret);
        var token = await database.ApiTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (token?.User is not { IsDisabled: false } user || token.ExpiresAt <= now)
        {
            return null;
        }

        if (token.LastUsedAt is null || now - token.LastUsedAt >= TouchInterval)
        {
            token.LastUsedAt = now;
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SignedInUser(user.Id, user.UserName, user.DisplayName, user.IsAdmin, user.MustChangePassword, SessionId: null);
    }

    private static UseCaseError UserNotFound => UseCaseError.NotFound("The user does not exist.");

    private static TokenView View(ApiToken token, DateTimeOffset now) =>
        new(token.Id, token.Name, token.Hint, token.CreatedAt, token.ExpiresAt, token.LastUsedAt, token.ExpiresAt <= now);

    private static AuditEntry Entry(string action, Guid userId, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "user",
        TargetId = userId.ToString(),
        Details = AuditEntry.DetailsOf(details),
    };
}
