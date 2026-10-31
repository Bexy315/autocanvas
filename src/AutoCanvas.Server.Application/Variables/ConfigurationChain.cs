using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Identity;

namespace AutoCanvas.Server.Application.Variables;

/// <summary>
/// The resolution rule of ADR-0013 as pure functions: instance, then folders from the top down,
/// then the automation; the nearest definition of a name wins.
/// </summary>
internal static class ConfigurationChain
{
    /// <summary>
    /// The folders of a chain whose values apply below them. A restricted folder cuts the chain:
    /// someone granted only there must not use values of the folders above it, so only the nearest
    /// restricted folder and those below it count. Instance values always apply.
    /// </summary>
    public static IReadOnlyList<FolderChainEntry> ApplyingFolders(IReadOnlyList<FolderChainEntry> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var cut = -1;
        for (var i = 0; i < chain.Count; i++)
        {
            if (chain[i].IsRestricted)
            {
                cut = i;
            }
        }

        return cut <= 0 ? chain : [.. chain.Skip(cut)];
    }

    /// <summary>
    /// Every name with all its definitions in resolution order, the winner last; names compare
    /// regardless of case, as expressions read them.
    /// </summary>
    public static IReadOnlyList<ConfigurationEntry> Merge(IEnumerable<(string Name, ConfigurationSource Source)> definitions) =>
        [.. definitions
            .GroupBy(d => IdentityNames.Normalize(d.Name), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var sources = g.Select(d => d.Source).ToList();
                return new ConfigurationEntry(g.Last().Name, sources, sources[^1]);
            })];
}
