namespace AutoCanvas.Server.Application.Runs;

/// <summary>How an attempt ended.</summary>
public enum AttemptOutcome
{
    /// <summary>The job succeeded.</summary>
    Succeeded,

    /// <summary>The job failed; it is retried if its <c>retry</c> allows.</summary>
    Failed,

    /// <summary>The job was cancelled.</summary>
    Cancelled,
}
