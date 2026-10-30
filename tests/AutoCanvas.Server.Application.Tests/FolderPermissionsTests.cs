using AutoCanvas.Persistence.Folders;
using AutoCanvas.Server.Application.Folders;

namespace AutoCanvas.Server.Application.Tests;

public sealed class FolderPermissionsTests
{
    // A tree /a/b/c/d; the letters in "restricted" mark restricted folders, grants are "folder:role".
    [Theory]
    [InlineData("", "", "d", FolderRole.None)]
    [InlineData("", "a:Viewer", "d", FolderRole.Viewer)]
    [InlineData("", "a:Viewer,c:Editor", "d", FolderRole.Editor)]
    [InlineData("", "a:Owner,c:Viewer", "d", FolderRole.Owner)]
    [InlineData("", "d:Operator", "c", FolderRole.None)]
    [InlineData("c", "a:Owner", "d", FolderRole.None)]
    [InlineData("c", "a:Owner", "b", FolderRole.Owner)]
    [InlineData("c", "a:Owner,c:Viewer", "d", FolderRole.Viewer)]
    [InlineData("c", "a:Owner,d:Editor", "d", FolderRole.Editor)]
    [InlineData("b,d", "b:Owner,c:Editor", "d", FolderRole.None)]
    [InlineData("b,d", "b:Owner,c:Editor", "c", FolderRole.Owner)]
    [InlineData("d", "d:Viewer,d:Editor", "d", FolderRole.Editor)]
    public void Roles_are_inherited_downwards_until_a_restricted_folder(string restricted, string grants, string target, FolderRole expected)
    {
        var restrictedNames = restricted.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var tree = "abcd".Select(c => new FolderChainEntry(Id(c), c.ToString(), restrictedNames.Contains(c.ToString()))).ToList();
        var facts = grants.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(g => g.Split(':'))
            .Select(g => new FolderGrantFact(Id(g[0][0]), Enum.Parse<FolderRole>(g[1])))
            .ToList();
        var chain = tree.Take("abcd".IndexOf(target[0], StringComparison.Ordinal) + 1).ToList();

        FolderPermissions.EffectiveRole(chain, facts, isAdmin: false).ShouldBe(expected);
    }

    [Fact]
    public void Admins_own_every_folder_even_restricted_ones() =>
        FolderPermissions.EffectiveRole([new FolderChainEntry(Id('a'), "a", IsRestricted: true)], [], isAdmin: true).ShouldBe(FolderRole.Owner);

    [Fact]
    public void An_empty_chain_grants_nothing() =>
        FolderPermissions.EffectiveRole([], [], isAdmin: true).ShouldBe(FolderRole.None);

    [Fact]
    public void Roles_are_ordered_by_power()
    {
        Enum.GetValues<FolderRole>().ShouldBe([FolderRole.None, FolderRole.Viewer, FolderRole.Operator, FolderRole.Editor, FolderRole.Owner]);
        (FolderRole.Owner > FolderRole.Editor && FolderRole.Editor > FolderRole.Operator && FolderRole.Operator > FolderRole.Viewer).ShouldBeTrue();
    }

    private static Guid Id(char name) => new($"00000000-0000-0000-0000-00000000000{name}");
}
