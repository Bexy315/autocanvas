using AutoCanvas.Persistence;
using AutoCanvas.Server.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AutoCanvas.Server.Application.Identity;

/// <summary>
/// Finds people and groups by name, for anyone signed in: what a folder's owner needs to share it
/// without seeing the list of all accounts. Only names come back, never e-mail addresses; disabled
/// accounts are left out.
/// </summary>
public sealed class DirectoryService(AutoCanvasDbContext database)
{
    /// <summary>The fewest characters a search needs, so it never lists everyone.</summary>
    public const int MinQueryLength = 2;

    /// <summary>The most users and the most groups one search returns.</summary>
    public const int MaxResults = 20;

    // Matches are read in a larger batch and ordered here: names that start with the query first.
    private const int Candidates = 100;

    /// <summary>Users whose name or display name contains <paramref name="query"/>, and groups whose name does.</summary>
    public async Task<UseCaseResult<DirectoryView>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length < MinQueryLength)
        {
            return UseCase.Failure<DirectoryView>(UseCaseError.Invalid("query", $"Type at least {MinQueryLength} characters."));
        }

        var normalized = IdentityNames.Normalize(text);
        // Display names are not stored normalized; the database upper-cases them (SQL upper()), which
        // no overload with a culture or a comparison can be translated to.
#pragma warning disable CA1304, CA1311, CA1862, MA0011 // translated to SQL, see above
        var users = await database.Users.AsNoTracking()
            .Where(u => !u.IsDisabled && (u.NormalizedUserName.Contains(normalized) || u.DisplayName.ToUpper().Contains(normalized)))
            .OrderBy(u => u.NormalizedUserName)
            .Take(Candidates)
            .Select(u => new UserSummary(u.Id, u.UserName, u.DisplayName))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore CA1304, CA1311, CA1862, MA0011
        var groups = await database.Groups.AsNoTracking()
            .Where(g => g.NormalizedName.Contains(normalized))
            .OrderBy(g => g.NormalizedName)
            .Take(Candidates)
            .Select(g => new GroupSummary(g.Id, g.Name))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return UseCase.Success(new DirectoryView(
            [.. users.OrderBy(u => StartsWith(u.UserName, text) || StartsWith(u.DisplayName, text) ? 0 : 1).Take(MaxResults)],
            [.. groups.OrderBy(g => StartsWith(g.Name, text) ? 0 : 1).Take(MaxResults)]));
    }

    private static bool StartsWith(string name, string text) => name.StartsWith(text, StringComparison.OrdinalIgnoreCase);
}
