namespace AutoCanvas.Server.Application.Triggers;

/// <summary>What one round of the trigger dispatcher did, for the server's log.</summary>
public sealed class TriggerRound
{
    private readonly List<string> started = [];
    private readonly List<string> skipped = [];

    /// <summary>The runs that started, as <c>'automation' #number</c>.</summary>
    public IReadOnlyList<string> Started => started;

    /// <summary>Why triggers that fired started nothing, such as a single run already active.</summary>
    public IReadOnlyList<string> Skipped => skipped;

    internal void Start(string run) => started.Add(run);

    internal void Skip(string reason) => skipped.Add(reason);
}
