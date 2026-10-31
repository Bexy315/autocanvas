namespace AutoCanvas.Server.Application.Runners;

/// <summary>Where a runner executes steps.</summary>
public enum RunnerExecutor
{
    /// <summary>Each job runs in a container; jobs with an image or services need this.</summary>
    Container,

    /// <summary>Steps run directly on the runner's host.</summary>
    Host,
}
