using AutoCanvas.Server.Application.Identity;

namespace AutoCanvas.Server.Api.Setup;

/// <summary>The result of the setup; the response also signs the new admin in.</summary>
/// <param name="Me">The new admin, signed in.</param>
/// <param name="MasterKey">The generated master key, unless the operator configured one.</param>
/// <param name="RunnerTokenWritten">Whether the bundled runner was handed a registration token.</param>
/// <param name="ExampleAutomationId">The example automation, when one was created.</param>
internal sealed record SetupCompleted(MeView Me, MasterKeyBackup? MasterKey, bool RunnerTokenWritten, Guid? ExampleAutomationId);
