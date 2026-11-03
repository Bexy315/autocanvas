using AutoCanvas.Server.Application.Identity;

namespace AutoCanvas.Server.Application.Setup;

/// <summary>What the setup created.</summary>
/// <param name="Admin">The first admin.</param>
/// <param name="RunnerTokenWritten">Whether a registration token was handed to the bundled runner.</param>
/// <param name="ExampleAutomationId">The example automation, when one was created.</param>
public sealed record SetupOutcome(UserView Admin, bool RunnerTokenWritten, Guid? ExampleAutomationId);
