using AutoCanvas.Server.Application.Instance;

namespace AutoCanvas.Server.Api.Setup;

/// <summary>What the setup asks about, as it stands.</summary>
/// <param name="PublicAddress">The address people reach the server at; locked when the configuration sets it.</param>
/// <param name="RunnerAddress">The address runners reach it at; locked when the configuration sets it.</param>
/// <param name="BundledRunner">Whether a runner came with the package and can be handed a token.</param>
/// <param name="MasterKey">Where the master key comes from.</param>
internal sealed record SetupDetails(SettingValue PublicAddress, SettingValue RunnerAddress, bool BundledRunner, MasterKeyOrigin MasterKey);
