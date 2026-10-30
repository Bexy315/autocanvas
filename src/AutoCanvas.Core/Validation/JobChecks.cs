using AutoCanvas.Core.Diagnostics;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Core.Validation;

/// <summary>Checks within one job: its cases or steps, and every expression it contains.</summary>
internal static class JobChecks
{
    public static void Check(JobDefinition job, IReadOnlySet<string> finished, ReferenceChecker references, ValidationContext context)
    {
        var path = $"jobs.{job.Id}";
        var jobScope = new ExpressionScope($"job '{job.Id}'", Variables: true, Jobs: finished, Steps: null);
        references.Condition(job.Condition, $"{path}.if", jobScope);

        switch (job)
        {
            case SwitchJob switchJob:
                CheckCases(switchJob, path, context);
                references.Text(switchJob.Expression, $"{path}.expression", jobScope);
                break;
            case StepJob stepJob:
                CheckStepJob(stepJob, path, jobScope, references, context);
                break;
        }
    }

    private static void CheckCases(SwitchJob job, string path, ValidationContext context)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var hasDefault = false;
        for (var i = 0; i < job.Cases.Count; i++)
        {
            var item = job.Cases[i];
            if (!names.Add(item.Name))
            {
                context.Error(DiagnosticCodes.InvalidCases, $"{path}.cases[{i}].name", $"There is already a case named '{item.Name}'.");
            }

            if (item.IsDefault && hasDefault)
            {
                context.Error(DiagnosticCodes.InvalidCases, $"{path}.cases[{i}]", "A switch can have only one default case.");
            }

            hasDefault |= item.IsDefault;
        }
    }

    private static void CheckStepJob(StepJob job, string path, ExpressionScope jobScope, ReferenceChecker references, ValidationContext context)
    {
        references.Text(job.Image, $"{path}.image", jobScope);
        for (var i = 0; i < job.RunsOn.Count; i++)
        {
            references.Text(job.RunsOn[i], $"{path}.runs_on[{i}]", jobScope);
        }

        CheckEnvironment(job.Env, $"{path}.env", jobScope, references);
        CheckCheckout(job.Checkout, $"{path}.checkout", jobScope, references);
        foreach (var service in job.Services)
        {
            var servicePath = $"{path}.services.{service.Name}";
            references.Text(service.Image, $"{servicePath}.image", jobScope);
            references.Text(service.Ready, $"{servicePath}.ready", jobScope);
            CheckEnvironment(service.Env, $"{servicePath}.env", jobScope, references);
        }

        var allSteps = CheckSteps(job, path, jobScope, references, context);
        var outputScope = jobScope with { Steps = allSteps };
        foreach (var (name, value) in job.Outputs)
        {
            references.Text(value, $"{path}.outputs.{name}", outputScope);
        }

        for (var i = 0; i < job.Reports.Count; i++)
        {
            CheckReport(job.Reports[i], $"{path}.reports[{i}]", jobScope, references);
        }
    }

    /// <summary>Checks each step with the steps before it in scope; returns the ids of all steps.</summary>
    private static HashSet<string> CheckSteps(StepJob job, string path, ExpressionScope jobScope, ReferenceChecker references, ValidationContext context)
    {
        var earlier = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var stepPath = $"{path}.steps[{i}]";
            StepChecks.Check(step, stepPath, jobScope with { Steps = new HashSet<string>(earlier, StringComparer.Ordinal) }, references);
            if (step.Id is { } id && !earlier.Add(id))
            {
                context.Error(DiagnosticCodes.DuplicateStepId, $"{stepPath}.id", $"Another step of this job already has the id '{id}'.");
            }
        }

        return earlier;
    }

    private static void CheckCheckout(CheckoutSetting checkout, string path, ExpressionScope scope, ReferenceChecker references)
    {
        for (var i = 0; i < checkout.Repositories.Count; i++)
        {
            var repository = checkout.Repositories[i];
            references.Text(repository.Repository, $"{path}[{i}].repository", scope);
            references.Text(repository.Ref, $"{path}[{i}].ref", scope);
            references.Text(repository.Path, $"{path}[{i}].path", scope);
        }
    }

    private static void CheckReport(ReportDefinition report, string path, ExpressionScope scope, ReferenceChecker references)
    {
        references.Text(report.Name, $"{path}.name", scope);
        if (report.Paths.Count == 1)
        {
            references.Text(report.Paths[0], $"{path}.path", scope);
            return;
        }

        for (var i = 0; i < report.Paths.Count; i++)
        {
            references.Text(report.Paths[i], $"{path}.path[{i}]", scope);
        }
    }

    internal static void CheckEnvironment(IReadOnlyDictionary<string, string> env, string path, ExpressionScope scope, ReferenceChecker references)
    {
        foreach (var (name, value) in env)
        {
            references.Text(value, $"{path}.{name}", scope);
        }
    }
}
