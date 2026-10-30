using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Parsing;

/// <summary>The keys every kind of job has.</summary>
internal sealed record JobHeader(string Id, string? Name, IReadOnlyList<JobNeed> Needs, string? Condition);
