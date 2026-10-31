using AutoCanvas.Server.Application.Folders;
using AutoCanvas.Server.Application.Variables;

namespace AutoCanvas.Server.Application.Tests;

public sealed class ConfigurationChainTests
{
    private static readonly FolderChainEntry Team = new(Guid.CreateVersion7(), "Team", IsRestricted: false);
    private static readonly FolderChainEntry Prod = new(Guid.CreateVersion7(), "Prod", IsRestricted: true);
    private static readonly FolderChainEntry App = new(Guid.CreateVersion7(), "App", IsRestricted: false);
    private static readonly FolderChainEntry Locked = new(Guid.CreateVersion7(), "Locked", IsRestricted: true);

    public static TheoryData<FolderChainEntry[], string[]> Chains => new()
    {
        { [Team, App], ["Team", "App"] },
        { [Prod, App], ["Prod", "App"] },
        { [Team, Prod, App], ["Prod", "App"] },
        { [Team, Prod, App, Locked], ["Locked"] },
        { [], [] },
    };

    [Theory]
    [MemberData(nameof(Chains))]
    public void A_restricted_folder_cuts_off_the_folders_above_it(FolderChainEntry[] chain, string[] applying) =>
        ConfigurationChain.ApplyingFolders(chain).Select(f => f.Name).ShouldBe(applying);

    [Fact]
    public void The_nearest_definition_wins_regardless_of_case()
    {
        var instance = new ConfigurationSource(ConfigurationScopeKind.Instance, null, null, "1");
        var folder = new ConfigurationSource(ConfigurationScopeKind.Folder, Team.Id, "Team", "2");
        var file = new ConfigurationSource(ConfigurationScopeKind.Automation, null, null, "3");

        var entries = ConfigurationChain.Merge([("region", instance), ("Region", folder), ("REGION", file), ("only", instance)]);

        entries.Select(e => e.Name).ShouldBe(["only", "REGION"]);
        entries[1].Definitions.ShouldBe([instance, folder, file]);
        entries[1].Winner.ShouldBe(file);
    }
}
