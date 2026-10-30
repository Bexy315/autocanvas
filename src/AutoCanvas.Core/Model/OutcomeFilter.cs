namespace AutoCanvas.Core.Model;

/// <summary>Which outcome of a job or automation something reacts to.</summary>
public enum OutcomeFilter
{
    /// <summary>Only success.</summary>
    Success,

    /// <summary>Only failure.</summary>
    Failure,

    /// <summary>Any outcome.</summary>
    Always,
}
