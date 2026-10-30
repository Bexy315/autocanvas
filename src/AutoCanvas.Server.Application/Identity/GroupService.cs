using AutoCanvas.Persistence;
using AutoCanvas.Persistence.Identity;
using AutoCanvas.Server.Application.Audit;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>Administration of groups and their members. Callers must be instance admins (checked by the host).</summary>
public sealed class GroupService(AutoCanvasDbContext database, IAuditLog audit, TimeProvider time)
{
    /// <summary>Creates a group.</summary>
    public async Task<UseCaseResult<GroupDetails>> CreateAsync(NewGroup request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((IdentityNames.CheckGroupName(request.Name) ?? CheckDescription(request.Description)) is { } invalid)
        {
            return UseCase.Failure<GroupDetails>(invalid);
        }

        var name = request.Name.Trim();
        var normalized = IdentityNames.Normalize(name);
        if (await database.Groups.AnyAsync(g => g.NormalizedName == normalized, cancellationToken).ConfigureAwait(false))
        {
            return UseCase.Failure<GroupDetails>(NameTaken(name));
        }

        var now = time.GetUtcNow();
        var group = new Group
        {
            Id = Guid.CreateVersion7(now),
            Name = name,
            NormalizedName = normalized,
            Description = NullIfEmpty(request.Description),
            CreatedAt = now,
        };
        database.Groups.Add(group);
        audit.Record(Entry(AuditActions.GroupCreated, group, ("name", group.Name)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success<GroupDetails>(new GroupDetails(group.Id, group.Name, group.Description, group.CreatedAt, []));
    }

    /// <summary>One group with its members.</summary>
    public async Task<UseCaseResult<GroupDetails>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await database.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return UseCase.Failure<GroupDetails>(GroupNotFound);
        }

        var members = await database.GroupMembers.AsNoTracking()
            .Where(m => m.GroupId == id)
            .OrderBy(m => m.User!.NormalizedUserName)
            .Select(m => new UserSummary(m.UserId, m.User!.UserName, m.User.DisplayName))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return UseCase.Success<GroupDetails>(new GroupDetails(group.Id, group.Name, group.Description, group.CreatedAt, members));
    }

    /// <summary>Groups ordered by name, one page at a time.</summary>
    public async Task<UseCaseResult<Page<GroupView>>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var limit = page.ResolveLimit();
        var after = NameCursor.Decode(page.Cursor);
        if ((limit.Error ?? after.Error) is { } invalid)
        {
            return UseCase.Failure<Page<GroupView>>(invalid);
        }

        var query = database.Groups.AsNoTracking();
        if (after.Value is { } last)
        {
            query = query.Where(g => g.NormalizedName.CompareTo(last) > 0);
        }

        var groups = await query
            .OrderBy(g => g.NormalizedName)
            .Take(limit.Value + 1)
            .Select(g => new { g.NormalizedName, View = new GroupView(g.Id, g.Name, g.Description, g.CreatedAt, g.Members.Count) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var items = groups.Take(limit.Value).Select(g => g.View).ToList();
        var next = groups.Count > limit.Value ? NameCursor.Encode(groups[limit.Value - 1].NormalizedName) : null;
        return UseCase.Success<Page<GroupView>>(new Page<GroupView>(items, next));
    }

    /// <summary>Renames a group or changes its description.</summary>
    public async Task<UseCaseResult<GroupDetails>> UpdateAsync(Guid id, GroupChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (((changes.Name is null ? null : IdentityNames.CheckGroupName(changes.Name)) ?? CheckDescription(changes.Description)) is { } invalid)
        {
            return UseCase.Failure<GroupDetails>(invalid);
        }

        var group = await database.Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return UseCase.Failure<GroupDetails>(GroupNotFound);
        }

        var changed = new List<string>();
        if (changes.Name?.Trim() is { } name && !string.Equals(name, group.Name, StringComparison.Ordinal))
        {
            var normalized = IdentityNames.Normalize(name);
            if (await database.Groups.AnyAsync(g => g.Id != id && g.NormalizedName == normalized, cancellationToken).ConfigureAwait(false))
            {
                return UseCase.Failure<GroupDetails>(NameTaken(name));
            }

            group.Name = name;
            group.NormalizedName = normalized;
            changed.Add("name");
        }

        if (changes.Description is { } description && !string.Equals(NullIfEmpty(description), group.Description, StringComparison.Ordinal))
        {
            group.Description = NullIfEmpty(description);
            changed.Add("description");
        }

        if (changed.Count > 0)
        {
            audit.Record(Entry(AuditActions.GroupUpdated, group, ("changed", changed), ("name", group.Name)));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a group; its members stay, only the membership goes.</summary>
    public async Task<UseCaseResult<Done>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await database.Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return UseCase.Failure<Done>(GroupNotFound);
        }

        database.Groups.Remove(group);
        audit.Record(Entry(AuditActions.GroupDeleted, group, ("name", group.Name)));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Done.Result;
    }

    /// <summary>Adds a user to a group; adding a member twice changes nothing.</summary>
    public async Task<UseCaseResult<Done>> AddMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var (group, user, error) = await FindAsync(groupId, userId, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return UseCase.Failure<Done>(error);
        }

        if (!await database.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken).ConfigureAwait(false))
        {
            database.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId });
            audit.Record(Entry(AuditActions.GroupMemberAdded, group!, ("userId", user!.Id), ("userName", user.UserName)));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Done.Result;
    }

    /// <summary>Removes a user from a group; removing a non-member changes nothing.</summary>
    public async Task<UseCaseResult<Done>> RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var (group, user, error) = await FindAsync(groupId, userId, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return UseCase.Failure<Done>(error);
        }

        var membership = await database.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (membership is not null)
        {
            database.GroupMembers.Remove(membership);
            audit.Record(Entry(AuditActions.GroupMemberRemoved, group!, ("userId", user!.Id), ("userName", user.UserName)));
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Done.Result;
    }

    private static UseCaseError GroupNotFound => UseCaseError.NotFound("The group does not exist.");

    private static UseCaseError NameTaken(string name) => UseCaseError.Conflict($"A group named '{name}' exists already.", "name");

    private static UseCaseError? CheckDescription(string? description) =>
        description is not null && description.Trim().Length > Group.MaxDescriptionLength
            ? UseCaseError.Invalid("description", $"The description must have at most {Group.MaxDescriptionLength} characters.")
            : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AuditEntry Entry(string action, Group group, params (string Key, object? Value)[] details) => new(action)
    {
        TargetType = "group",
        TargetId = group.Id.ToString(),
        Details = AuditEntry.DetailsOf(details),
    };

    private async Task<(Group? Group, User? User, UseCaseError? Error)> FindAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var group = await database.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken).ConfigureAwait(false);
        if (group is null)
        {
            return (null, null, GroupNotFound);
        }

        var user = await database.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
        return user is null ? (group, null, UseCaseError.NotFound("The user does not exist.")) : (group, user, null);
    }
}
