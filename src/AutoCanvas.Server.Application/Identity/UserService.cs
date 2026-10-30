using System.Net.Mail;
using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Identity;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>Administration of local accounts. Callers must be instance admins (checked by the host).</summary>
public sealed class UserService(AutoCanvasDbContext database, PasswordHasher hasher, IAuditLog audit, TimeProvider time)
{
    /// <summary>Creates an account.</summary>
    public async Task<UseCaseResult<UserView>> CreateAsync(NewUser request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((IdentityNames.CheckUserName(request.UserName) ?? CheckDisplayName(request.DisplayName) ?? CheckEmail(request.Email)
            ?? PasswordPolicy.Check("password", request.Password, request.UserName)) is { } invalid)
        {
            return UseCase.Failure<UserView>(invalid);
        }

        var normalized = IdentityNames.Normalize(request.UserName);
        if (await database.Users.AnyAsync(u => u.NormalizedUserName == normalized, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<UserView>(UseCaseError.Conflict($"The user name '{request.UserName}' is taken.", "userName"));
        }

        var now = time.GetUtcNow();
        var user = new User
        {
            Id = Guid.CreateVersion7(now),
            UserName = request.UserName,
            NormalizedUserName = normalized,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.UserName : request.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            PasswordHash = hasher.Hash(request.Password),
            IsAdmin = request.IsAdmin,
            MustChangePassword = request.MustChangePassword,
            CreatedAt = now,
            PasswordChangedAt = now,
        };
        database.Users.Add(user);
        audit.Record(Entry(AuditActions.UserCreated, user, ("userName", user.UserName), ("isAdmin", user.IsAdmin)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success<UserView>(UserView.From(user));
    }

    /// <summary>
    /// Creates the first admin, unless any account exists already; then nothing happens and
    /// <see langword="null"/> is returned. Used by the bootstrap settings on start.
    /// </summary>
    public async Task<UseCaseResult<UserView>?> CreateFirstAdminAsync(string userName, string password, CancellationToken cancellationToken)
    {
        if (await database.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await CreateAsync(new NewUser(userName, password) { IsAdmin = true }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One user.</summary>
    public async Task<UseCaseResult<UserView>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false) is { } user
            ? UseCase.Success<UserView>(UserView.From(user))
            : UseCase.Failure<UserView>(UserNotFound);

    /// <summary>One user by user name, regardless of case.</summary>
    public async Task<UseCaseResult<UserView>> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        var normalized = IdentityNames.Normalize(userName);
        return await database.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, cancellationToken).ConfigureAwait(false) is { } user
            ? UseCase.Success<UserView>(UserView.From(user))
            : UseCase.Failure<UserView>(UseCaseError.NotFound($"There is no user '{userName}'."));
    }

    /// <summary>Users ordered by user name, one page at a time.</summary>
    public async Task<UseCaseResult<Page<UserView>>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var limit = page.ResolveLimit();
        var after = NameCursor.Decode(page.Cursor);
        if ((limit.Error ?? after.Error) is { } invalid)
        {
            return UseCase.Failure<Page<UserView>>(invalid);
        }

        var query = database.Users.AsNoTracking();
        if (after.Value is { } last)
        {
            query = query.Where(u => u.NormalizedUserName.CompareTo(last) > 0);
        }

        var users = await query.OrderBy(u => u.NormalizedUserName).Take(limit.Value + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = users.Take(limit.Value).Select(UserView.From).ToList();
        var next = users.Count > limit.Value ? NameCursor.Encode(users[limit.Value - 1].NormalizedUserName) : null;
        return UseCase.Success<Page<UserView>>(new Page<UserView>(items, next));
    }

    /// <summary>
    /// Changes display name, e-mail, admin flag or disabled flag. The last enabled admin cannot lose
    /// either; disabling a user ends their sessions.
    /// </summary>
    public async Task<UseCaseResult<UserView>> UpdateAsync(Guid id, UserChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if ((CheckDisplayName(changes.DisplayName) ?? CheckEmail(changes.Email)) is { } invalid)
        {
            return UseCase.Failure<UserView>(invalid);
        }

        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UseCase.Failure<UserView>(UserNotFound);
        }

        var losesAdmin = (changes.IsAdmin == false || changes.IsDisabled == true) && user is { IsAdmin: true, IsDisabled: false };
        if (losesAdmin && await IsLastAdminAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<UserView>(LastAdmin);
        }

        var changed = new List<string>();
        if (changes.DisplayName is { } displayName && !string.Equals(displayName.Trim(), user.DisplayName, StringComparison.Ordinal))
        {
            user.DisplayName = displayName.Trim();
            changed.Add("displayName");
        }

        if (changes.Email is { } email && !string.Equals(NullIfEmpty(email), user.Email, StringComparison.Ordinal))
        {
            user.Email = NullIfEmpty(email);
            changed.Add("email");
        }

        if (changes.IsAdmin is { } isAdmin && isAdmin != user.IsAdmin)
        {
            user.IsAdmin = isAdmin;
            changed.Add("isAdmin");
        }

        if (changes.IsDisabled is { } isDisabled && isDisabled != user.IsDisabled)
        {
            user.IsDisabled = isDisabled;
            changed.Add("isDisabled");
            if (isDisabled)
            {
                await EndSessionsAsync(user.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        if (changed.Count > 0)
        {
            audit.Record(Entry(AuditActions.UserUpdated, user, ("changed", changed), ("isAdmin", user.IsAdmin), ("isDisabled", user.IsDisabled)));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return UseCase.Success<UserView>(UserView.From(user));
    }

    /// <summary>Deletes an account with its sessions, tokens and memberships. Not the last enabled admin.</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UseCase.Failure<Done>(UserNotFound);
        }

        if (user is { IsAdmin: true, IsDisabled: false } && await IsLastAdminAsync(user.Id, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<Done>(LastAdmin);
        }

        database.Users.Remove(user);
        audit.Record(Entry(AuditActions.UserDeleted, user, ("userName", user.UserName)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }

    /// <summary>
    /// Sets a password someone else chose: the user must change it at the next sign-in, and their
    /// sessions end.
    /// </summary>
    public async Task<UseCaseResult<UserView>> ResetPasswordAsync(Guid id, string password, CancellationToken cancellationToken)
    {
        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return UseCase.Failure<UserView>(UserNotFound);
        }

        if (PasswordPolicy.Check("password", password, user.UserName) is { } invalid)
        {
            return UseCase.Failure<UserView>(invalid);
        }

        user.PasswordHash = hasher.Hash(password);
        user.PasswordChangedAt = time.GetUtcNow();
        user.MustChangePassword = true;
        await EndSessionsAsync(user.Id, cancellationToken).ConfigureAwait(false);
        audit.Record(Entry(AuditActions.UserPasswordReset, user));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success<UserView>(UserView.From(user));
    }

    private static UseCaseError UserNotFound => UseCaseError.NotFound("The user does not exist.");

    private static UseCaseError LastAdmin =>
        UseCaseError.Conflict("This is the last enabled admin. Make another user an admin first.");

    private static UseCaseError? CheckDisplayName(string? displayName) =>
        displayName is not null && (displayName.Trim().Length is 0 or > User.MaxDisplayNameLength || displayName.Any(char.IsControl))
            ? UseCaseError.Invalid("displayName", $"The display name must have 1 to {User.MaxDisplayNameLength} characters and no control characters.")
            : null;

    private static UseCaseError? CheckEmail(string? email) =>
        !string.IsNullOrEmpty(email) && (email.Trim().Length > User.MaxEmailLength || !MailAddress.TryCreate(email.Trim(), out _))
            ? UseCaseError.Invalid("email", "The e-mail address is not valid.")
            : null;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuditEntry Entry(string action, User user, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "user",
        TargetId = user.Id.ToString(),
        Details = AuditEntry.DetailsOf(details),
    };

    private async Task<bool> IsLastAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        !await database.Users.AnyAsync(u => u.Id != userId && u.IsAdmin && !u.IsDisabled, cancellationToken).ConfigureAwait(false);

    private async Task EndSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        database.Sessions.RemoveRange(await database.Sessions.Where(s => s.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false));
}
