namespace AutoCanvas.Server.Tests;

/// <summary>
/// Tests that run the server's command line in this process. Every command builds a host, and
/// WebApplicationFactory finds the host it starts through a process-wide diagnostic listener: run
/// in parallel, a factory can pick up a command's host, which the command then disposes. So these
/// tests run alone, after the parallel ones.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CommandLineTests
{
    public const string Name = "Command line";
}
