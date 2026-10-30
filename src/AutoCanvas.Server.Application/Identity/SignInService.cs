using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Audit;
using AutoCanvas.Persistence.Identity;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>
/// Signs users in with user name and password. Every failure looks the same to the caller and takes
/// the same time, whether the name is unknown, the password wrong or the user disabled (ADR-0011).
/// </summary>
public sealed class SignInService(
    AutoCanvasDbContext database,
    PasswordHasher hasher,
    LoginThrottle throttle,
    SessionService sessions,
    AccountService accounts,
    IAuditLog audit,
    TimeProvider time)
{
    private const string Refused = "The user name or the password is wrong, or the account is disabled.";

    /// <summary>Checks the credentials and starts a session.</summary>
    public async Task<UseCaseResult<SignInResult>> SignInAsync(string userName, string password, string? clientAddress, string? userAgent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

        var normalized = IdentityNames.Normalize(userName);
        if (throttle.IsLocked(normalized))
        {
            await RecordFailureAsync(userName, "locked", cancellationToken).ConfigureAwait(false);
            return UseCase.Failure<SignInResult>(new UseCaseError(
                UseCaseErrorKind.TooManyRequests,
                $"Too many failed sign-ins for this user name. Try again in {LoginThrottle.LockDuration.TotalMinutes:0} minutes."));
        }

        // Longer passwords are refused before hashing, so a request cannot make the server hash megabytes.
        var user = password.Length <= PasswordPolicy.MaxLength
            ? await database.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, cancellationToken).ConfigureAwait(false)
            : null;
        var verification = PasswordVerification.Failed;
        if (user is null)
        {
            hasher.VerifyNothing(password.Length <= PasswordPolicy.MaxLength ? password : string.Empty);
        }
        else
        {
            verification = hasher.Verify(password, user.PasswordHash);
        }

        if (user is null || verification == PasswordVerification.Failed || user.IsDisabled)
        {
            throttle.RecordFailure(normalized);
            var reason = user is null ? "unknown user" : user.IsDisabled ? "disabled" : "wrong password";
            await RecordFailureAsync(userName, reason, cancellationToken).ConfigureAwait(false);
            return UseCase.Failure<SignInResult>(new UseCaseError(UseCaseErrorKind.Unauthorized, Refused));
        }

        throttle.Reset(normalized);
        if (verification == PasswordVerification.SucceededRehashNeeded)
        {
            user.PasswordHash = hasher.Hash(password);
        }

        user.LastSignInAt = time.GetUtcNow();
        var (token, session) = sessions.Issue(user, clientAddress, userAgent);
        audit.Record(new AuditEntry(AuditActions.SignedIn)
        {
            Actor = new AuditActor(AuditActorKind.User, user.Id, user.UserName),
            TargetType = "user",
            TargetId = user.Id.ToString(),
            Details = AuditEntry.DetailsOf(("sessionId", session.Id)),
        });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var me = await accounts.GetMeAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new SignInResult(token, session.ExpiresAt, me.Value!));
    }

    private async Task RecordFailureAsync(string userName, string reason, CancellationToken cancellationToken)
    {
        audit.Record(new AuditEntry(AuditActions.SignInFailed)
        {
            Details = AuditEntry.DetailsOf(("userName", userName.Length > User.MaxUserNameLength ? userName[..User.MaxUserNameLength] : userName), ("reason", reason)),
        });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
