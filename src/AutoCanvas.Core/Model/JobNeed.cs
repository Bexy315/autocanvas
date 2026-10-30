namespace AutoCanvas.Core.Model;

/// <summary>A dependency of a job on another job.</summary>
/// <param name="Job">Id of the job that must finish first.</param>
/// <param name="On">Which outcome of that job lets this one start.</param>
/// <param name="Case">For a switch: the case that must have been activated, or <see langword="null"/>.</param>
public sealed record JobNeed(string Job, OutcomeFilter On, string? Case);
