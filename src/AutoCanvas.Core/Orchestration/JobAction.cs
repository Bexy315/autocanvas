namespace AutoCanvas.Core.Orchestration;

/// <summary>A decision about a job that the server carries out.</summary>
/// <param name="JobId">The job it concerns.</param>
public abstract record JobAction(string JobId);
