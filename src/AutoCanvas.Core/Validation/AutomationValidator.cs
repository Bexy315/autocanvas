using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Validation;

/// <summary>
/// Checks that the parts of a parsed automation fit together: needs point to jobs that can be
/// waited for, jobs do not need each other in a cycle, switch cases and step ids are unique, and
/// every expression is valid and only reads what is available where it stands.
/// </summary>
public static class AutomationValidator
{
    /// <summary>Validates <paramref name="automation"/>.</summary>
    /// <param name="automation">An automation as returned by the parser.</param>
    /// <param name="locations">Source positions by document path, from <see cref="Parsing.ParseResult.Locations"/>; positions are omitted without it.</param>
    /// <returns>All problems found; empty if the automation is valid.</returns>
    public static IReadOnlyList<Diagnostic> Validate(AutomationDefinition automation, IReadOnlyDictionary<string, SourceLocation>? locations = null)
    {
        ArgumentNullException.ThrowIfNull(automation);

        var context = new ValidationContext(locations ?? new Dictionary<string, SourceLocation>(StringComparer.Ordinal));
        var graph = new JobGraph(automation.Jobs);
        var acyclic = graph.Check(context);
        var references = new ReferenceChecker(graph, context);

        CheckTopLevel(automation, graph, references);
        foreach (var job in automation.Jobs)
        {
            // In a cycle "which jobs finished before this one" has no answer; the cycle is reported already.
            var finished = acyclic ? FinishedBefore(job, graph) : new HashSet<string>(graph.JobIds, StringComparer.Ordinal);
            JobChecks.Check(job, finished, references, context);
        }

        return context.Diagnostics;
    }

    private static void CheckTopLevel(AutomationDefinition automation, JobGraph graph, ReferenceChecker references)
    {
        var variables = new ExpressionScope("a variable", Variables: false, Jobs: null, Steps: null);
        foreach (var (name, value) in automation.Variables)
        {
            references.Text(value, $"variables.{name}", variables);
        }

        var runEnd = new ExpressionScope("changed_when", Variables: true, Jobs: new HashSet<string>(graph.JobIds, StringComparer.Ordinal), Steps: null);
        references.Condition(automation.ChangedWhen, "changed_when", runEnd);
    }

    /// <summary>A finally job runs after all others; any other job after its needs and their needs.</summary>
    private static IReadOnlySet<string> FinishedBefore(JobDefinition job, JobGraph graph) =>
        job is StepJob { RunsFinally: true }
            ? graph.JobIds.Where(id => !string.Equals(id, job.Id, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal)
            : graph.Ancestors(job.Id);
}
