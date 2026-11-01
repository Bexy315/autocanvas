namespace AutoCanvas.Persistence.Triggers;

/// <summary>Which results of the followed automation start a run (<c>on</c>).</summary>
public enum CompletionOutcome
{
    /// <summary>Only a run that succeeded.</summary>
    Success,

    /// <summary>Only a run that failed.</summary>
    Failure,

    /// <summary>Any finished run, also a cancelled one.</summary>
    Always,
}
