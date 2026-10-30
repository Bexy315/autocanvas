using AutoCanvas.Server.Hosting;

return await ServerCommandLine.RunAsync(args).ConfigureAwait(false);

/// <summary>
/// Entry point of the AutoCanvas server. Public only so that integration tests can host the
/// application in memory with <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "WebApplicationFactory<Program> in the tests needs a public entry point type.")]
public partial class Program;
