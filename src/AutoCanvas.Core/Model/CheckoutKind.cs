namespace AutoCanvas.Core.Model;

/// <summary>How a job's checkout was specified.</summary>
public enum CheckoutKind
{
    /// <summary>Not specified.</summary>
    Default,

    /// <summary>Turned off with <c>checkout: false</c>.</summary>
    Disabled,

    /// <summary>A list of repositories.</summary>
    Explicit,
}
