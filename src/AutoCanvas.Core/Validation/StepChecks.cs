using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Validation;

/// <summary>Checks the expressions of one step.</summary>
internal static class StepChecks
{
    /// <param name="step">The step.</param>
    /// <param name="path">Document path of the step.</param>
    /// <param name="scope">Scope with the earlier steps of the job.</param>
    /// <param name="references">The expression checker.</param>
    public static void Check(StepDefinition step, string path, ExpressionScope scope, ReferenceChecker references)
    {
        references.Condition(step.Condition, $"{path}.if", scope);
        JobChecks.CheckEnvironment(step.Env, $"{path}.env", scope, references);

        switch (step)
        {
            case ScriptStep script:
                references.Text(script.Run, $"{path}.run", scope);
                references.Text(script.WorkingDirectory, $"{path}.working_directory", scope);
                break;
            case NodeStep node:
                references.Json(node.Inputs, $"{path}.with", scope);

                // A node step's extra outputs are computed after it ran, so they may read its own outputs.
                var withSelf = node.Id is { } id ? scope with { Steps = new HashSet<string>(scope.Steps!, StringComparer.Ordinal) { id } } : scope;
                foreach (var (name, value) in node.Outputs)
                {
                    references.Text(value, $"{path}.outputs.{name}", withSelf);
                }

                break;
        }
    }
}
