namespace AutoCanvas.Core.Model;

/// <summary>Whether runs of an automation may overlap.</summary>
public enum Concurrency
{
    /// <summary>Runs start whenever they are triggered.</summary>
    Parallel,

    /// <summary>A run starts only when no other run of the automation is active (<c>concurrency: single</c>).</summary>
    Exclusive,
}
