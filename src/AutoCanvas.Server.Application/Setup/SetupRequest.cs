namespace AutoCanvas.Server.Application.Setup;

/// <summary>What the person setting up an installation chose (ADR-0020).</summary>
/// <param name="Admin">The first admin; they chose the password, so they need not change it.</param>
/// <param name="PublicAddress">The address the server is reached at, if known.</param>
/// <param name="RunnerAddress">The address runners reach it at, if known.</param>
/// <param name="BundledRunner">Hand a registration token to the runner that came with the package.</param>
/// <param name="Example">Create the example automation.</param>
public sealed record SetupRequest(SetupAdmin Admin, string? PublicAddress, string? RunnerAddress, bool BundledRunner, bool Example);
