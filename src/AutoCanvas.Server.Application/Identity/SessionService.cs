using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Persistence.Identity;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>
/// Sessions of signed-in browsers: issuing, checking on every request, listing and ending them.
/// Sessions end after <see cref="SessionLifetimeOptions.SessionIdleTimeout"/> without requests and after
/// <see cref="SessionLifetimeOptions.SessionLifetime"/> at the latest.
/// </summary>
public sealed class SessionService(AutoCanvasDbContext database, IAuditLog audit, IOptions<SessionLifetimeOptions> options, TimeProvider time)
{
    // Writing on every request would turn reads into writes; a minute is precise enough for expiry.
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Adds a session for the user to the pending changes and returns its token; the caller saves
    /// it together with the sign-in.
    /// </summary>
    internal (string Token, Session Session) Issue(User user, string? clientAddress, string? userAgent)
    {
        var now = time.GetUtcNow();
        var token = SecretTokens.Create();
        var session = new Session
        {
            Id = Guid.CreateVersion7(now),
            UserId = user.Id,
            TokenHash = SecretTokens.Hash(token),
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + options.Value.SessionLifetime,
            ClientAddress = clientAddress,
            UserAgent = userAgent is { Length: > AuditEvent.MaxUserAgentLength } ? userAgent[..AuditEvent.MaxUserAgentLength] : userAgent,
        };
        database.Sessions.Add(session);
        return (token, session);
    }

    /// <summary>
    /// The user behind a session token, or <see langword="null"/> when the token is unknown, the
    /// session has expired or the user is disabled. Expired sessions are removed.
    /// </summary>
    public async Task<SignedInUser?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        var hash = SecretTokens.Hash(token);
        var session = await database.Sessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken).ConfigureAwait(false);
        if (session?.User is not { } user)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (now >= session.ExpiresAt || now - session.LastSeenAt >= options.Value.SessionIdleTimeout)
        {
            database.Sessions.Remove(session);
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (user.IsDisabled)
        {
            return null;
        }

        if (now - session.LastSeenAt >= TouchInterval)
        {
            session.LastSeenAt = now;
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SignedInUser(user.Id, user.UserName, user.DisplayName, user.IsAdmin, user.MustChangePassword, session.Id);
    }

    /// <summary>The user's sessions that have not expired, newest first.</summary>
    public async Task<IReadOnlyList<SessionView>> ListAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var idleSince = now - options.Value.SessionIdleTimeout;
        var sessions = await database.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.ExpiresAt > now && s.LastSeenAt > idleSince)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. sessions.Select(s => new SessionView(s.Id, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.ClientAddress, s.UserAgent, s.Id == currentSessionId))];
    }

    /// <summary>Ends one of the user's sessions (sign-out, or ending a session elsewhere).</summary>
    public async Task<UseCaseResult<Done>> EndAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await database.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound("The session does not exist."));
        }

        database.Sessions.Remove(session);
        audit.Record(Entry(AuditActions.SignedOut, userId, ("sessionId", sessionId)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }

    /// <summary>Ends every session of the user ("sign out everywhere").</summary>
    public async Task<int> EndAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var sessions = await database.Sessions.Where(s => s.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false);
        database.Sessions.RemoveRange(sessions);
        audit.Record(Entry(AuditActions.SessionsEnded, userId, ("count", sessions.Count)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return sessions.Count;
    }

    private static AuditEntry Entry(string action, Guid userId, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "user",
        TargetId = userId.ToString(),
        Details = AuditEntry.DetailsOf(details),
    };
}
