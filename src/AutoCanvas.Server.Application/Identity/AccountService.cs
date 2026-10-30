using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>What signed-in users do with their own account.</summary>
public sealed class AccountService(AutoCanvasDbContext database, PasswordHasher hasher, IAuditLog audit, TimeProvider time)
{
    /// <summary>The user's account and groups.</summary>
    public async Task<UseCaseResult<MeView>> GetMeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UseCase.Failure<MeView>(UseCaseError.NotFound("The user does not exist."));
        }

        var groups = await database.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Group!.NormalizedName)
            .Select(m => new GroupSummary(m.GroupId, m.Group!.Name))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success(new MeView(UserView.From(user), groups));
    }

    /// <summary>
    /// Replaces the user's password after checking the current one. This lifts
    /// <c>mustChangePassword</c> and ends every other session of the user.
    /// </summary>
    public async Task<UseCaseResult<Done>> ChangePasswordAsync(
        Guid userId,
        Guid? currentSessionId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UseCase.Failure<Done>(UseCaseError.NotFound("The user does not exist."));
        }

        if (currentPassword is not { Length: <= PasswordPolicy.MaxLength } || hasher.Verify(currentPassword, user.PasswordHash) == PasswordVerification.Failed)
        {
            return UseCase.Failure<Done>(UseCaseError.Invalid("currentPassword", "The current password is wrong."));
        }

        if (PasswordPolicy.Check("newPassword", newPassword, user.UserName) is { } invalid)
        {
            return UseCase.Failure<Done>(invalid);
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return UseCase.Failure<Done>(UseCaseError.Invalid("newPassword", "The new password must differ from the current one."));
        }

        user.PasswordHash = hasher.Hash(newPassword);
        user.PasswordChangedAt = time.GetUtcNow();
        user.MustChangePassword = false;
        database.Sessions.RemoveRange(await database.Sessions
            .Where(s => s.UserId == userId && s.Id != currentSessionId)
            .ToListAsync(cancellationToken).ConfigureAwait(false));
        audit.Record(new AuditEntry(AuditActions.UserPasswordChanged) { TargetType = "user", TargetId = userId.ToString() });
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }
}
