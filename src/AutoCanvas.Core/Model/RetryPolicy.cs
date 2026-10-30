namespace AutoCanvas.Core.Model;

/// <summary>How a failed job is run again.</summary>
/// <param name="MaxAttempts">Attempts in total, including the first.</param>
/// <param name="Delay">Wait between attempts.</param>
public sealed record RetryPolicy(int MaxAttempts, TimeSpan Delay);
