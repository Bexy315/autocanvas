using System.Text.Json;
using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Expressions;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Validation;

/// <summary>
/// Checks that the expressions in texts and conditions are valid syntax and only read what exists
/// and is available at their place.
/// </summary>
internal sealed class ReferenceChecker(JobGraph graph, ValidationContext context)
{
    private static readonly HashSet<string> TriggerFields = new(StringComparer.Ordinal)
    {
        "event", "branch", "ref", "tag", "sha", "short_sha", "before", "pusher", "message", "body",
        "schedule", "scheduled_at", "automation", "run_id", "run_number", "result",
        "number", "title", "action", "head_branch", "base_branch", "author", "fork", "url",
    };

    private static readonly HashSet<string> RunFields = new(StringComparer.Ordinal)
    {
        "id", "automation_name", "triggered_by", "branch", "commit_sha", "commit_short_sha", "status",
    };

    /// <summary>Checks text that may contain <c>${{ … }}</c>.</summary>
    public void Text(string? text, string path, ExpressionScope scope)
    {
        if (text is null)
        {
            return;
        }

        var (template, errors) = Template.Parse(text);
        foreach (var error in errors)
        {
            context.Error(DiagnosticCodes.ExpressionSyntax, path, $"{error.Message} (at character {error.Offset + 1})");
        }

        foreach (var part in template.Parts.OfType<ExpressionPart>())
        {
            Expression(part.Expression, path, scope);
        }
    }

    /// <summary>Checks a condition (<c>if</c>, <c>changed_when</c>).</summary>
    public void Condition(string? text, string path, ExpressionScope scope)
    {
        if (text is null)
        {
            return;
        }

        var (expression, error) = ExpressionSyntax.ParseCondition(text);
        if (error is not null)
        {
            context.Error(DiagnosticCodes.ExpressionSyntax, path, $"{error.Message} (at character {error.Offset + 1})");
            return;
        }

        Expression(expression!, path, scope);
    }

    /// <summary>Checks every text inside a JSON value, such as a node step's inputs.</summary>
    public void Json(JsonElement value, string path, ExpressionScope scope)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                Text(value.GetString(), path, scope);
                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    Json(property.Value, $"{path}.{property.Name}", scope);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    Json(item, $"{path}[{index++}]", scope);
                }

                break;
        }
    }

    private void Expression(Expression expression, string path, ExpressionScope scope)
    {
        switch (expression)
        {
            case PathExpression reference:
                Reference(reference, path, scope);
                break;
            case NotExpression not:
                Expression(not.Operand, path, scope);
                break;
            case BinaryExpression binary:
                Expression(binary.Left, path, scope);
                Expression(binary.Right, path, scope);
                break;
        }
    }

    private void Reference(PathExpression reference, string path, ExpressionScope scope)
    {
        var problem = reference.Segments[0] switch
        {
            "vars" or "secrets" => VariableProblem(reference, scope),
            "trigger" => TriggerProblem(reference),
            "run" => RunProblem(reference),
            "jobs" => JobProblem(reference, scope),
            "steps" => StepProblem(reference, scope),
            _ => (DiagnosticCodes.UnknownReference, $"'{reference.Text}' is nothing an expression can read. Expressions read vars, secrets, steps, jobs, trigger and run."),
        };

        if (problem is var (code, message))
        {
            context.Error(code, path, message);
        }
    }

    private static (string Code, string Message)? VariableProblem(PathExpression reference, ExpressionScope scope)
    {
        if (reference.Segments.Count != 2)
        {
            return Unknown($"'{reference.Text}' should be {reference.Segments[0]}.NAME.");
        }

        return scope.Variables
            ? null
            : Unknown($"'{reference.Text}' cannot be read in {scope.Place}; only trigger and run are available there.");
    }

    private static (string Code, string Message)? TriggerProblem(PathExpression reference)
    {
        var segments = reference.Segments;
        if (segments.Count == 3 && segments[1] is "headers" or "query")
        {
            return null;
        }

        // A webhook's JSON body is read into by property names: trigger.body.repository.name.
        if (segments.Count > 2 && string.Equals(segments[1], "body", StringComparison.Ordinal))
        {
            return null;
        }

        return segments.Count == 2 && TriggerFields.Contains(segments[1])
            ? null
            : Unknown($"'{reference.Text}' is not something a trigger carries. It carries: {string.Join(", ", TriggerFields.Order(StringComparer.Ordinal).Select(f => "trigger." + f))}, trigger.body.FIELD, trigger.headers.NAME and trigger.query.NAME.");
    }

    private static (string Code, string Message)? RunProblem(PathExpression reference) =>
        reference.Segments.Count == 2 && RunFields.Contains(reference.Segments[1])
            ? null
            : Unknown($"'{reference.Text}' is not something a run knows. It knows: {string.Join(", ", RunFields.Order(StringComparer.Ordinal).Select(f => "run." + f))}.");

    private (string Code, string Message)? JobProblem(PathExpression reference, ExpressionScope scope)
    {
        var segments = reference.Segments;
        var isResult = segments.Count == 3 && segments[2] is "result";
        var isOutput = segments.Count == 4 && segments[2] is "outputs";
        if (!isResult && !isOutput)
        {
            return Unknown($"'{reference.Text}' should be jobs.JOB.outputs.NAME or jobs.JOB.result.");
        }

        if (scope.Jobs is null)
        {
            return Unknown($"'{reference.Text}' cannot be read in {scope.Place}; jobs are not available there.");
        }

        var job = graph.Find(segments[1]);
        if (job is null)
        {
            return Unknown($"'{reference.Text}' reads job '{segments[1]}', which does not exist.");
        }

        if (!scope.Jobs.Contains(job.Id))
        {
            return (DiagnosticCodes.UnreachableReference, $"'{reference.Text}' reads job '{job.Id}', which has not finished when {scope.Place} runs. Add '{job.Id}' to needs (directly or through another job).");
        }

        return isOutput && !(job is StepJob stepJob && stepJob.Outputs.ContainsKey(segments[3]))
            ? Unknown($"'{reference.Text}' reads output '{segments[3]}' of job '{job.Id}', which the job does not publish. Add it under the job's outputs.")
            : null;
    }

    private static (string Code, string Message)? StepProblem(PathExpression reference, ExpressionScope scope)
    {
        var segments = reference.Segments;
        if (segments is not [_, _, "outputs", _])
        {
            return Unknown($"'{reference.Text}' should be steps.STEP.outputs.NAME.");
        }

        if (scope.Steps is null)
        {
            return Unknown($"'{reference.Text}' cannot be read in {scope.Place}: step outputs are only visible inside their own job. Publish the value under the job's outputs and read jobs.JOB.outputs.NAME.");
        }

        return scope.Steps.Contains(segments[1])
            ? null
            : (DiagnosticCodes.UnreachableReference, $"'{reference.Text}' reads step '{segments[1]}', which is not an earlier step of this job with that id.");
    }

    private static (string Code, string Message) Unknown(string message) => (DiagnosticCodes.UnknownReference, message);
}
