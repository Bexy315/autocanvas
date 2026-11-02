using System.Text;
using System.Text.Json;
using AutoCanvas.Core.Model;

namespace AutoCanvas.Server.Application.Runs;

/// <summary>
/// Turns a step of the automation file into what its runner executes (ADR-0008): every expression
/// rendered with the run's values and the outputs of the steps before it. The job's environment
/// entries that read a secret, which the offer left out, come with every step; the secrets the step
/// reads become its <c>mask_values</c>.
/// </summary>
internal static class StepResolver
{
    public static ResolvedStep Resolve(StepJob job, int index, RunValues values, IReadOnlyCollection<string> shells)
    {
        var step = job.Steps[index];
        var reads = new SecretReads(values);
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, text) in job.Env)
        {
            var entry = new SecretReads(reads);
            var value = entry.Render(text);
            if (entry.Values.Count > 0)
            {
                env[name] = value;
            }
        }

        foreach (var (name, text) in step.Env)
        {
            env[name] = reads.Render(text);
        }

        ResolvedScript? script = null;
        ResolvedNode? node = null;
        switch (step)
        {
            case ScriptStep run:
                script = new ResolvedScript(Shell(run.Shell, shells), reads.Render(run.Run), run.WorkingDirectory is null ? null : reads.Render(run.WorkingDirectory), DefaultShell: run.Shell is null);
                break;
            case NodeStep use:
                node = new ResolvedNode(use.Type, Inputs(use.Inputs, reads));
                break;
            default:
                throw new InvalidOperationException($"Unknown step kind {step.GetType().Name}.");
        }

        return new ResolvedStep(
            index,
            step.Id,
            NameOf(step, index),
            step.TimeoutMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
            env,
            [.. reads.Values],
            script,
            node);
    }

    /// <summary>How the step is called in the run: its name, else its id, else its position.</summary>
    public static string NameOf(StepDefinition step, int index) => step.Name ?? step.Id ?? $"Step {index + 1}";

    /// <summary>The step's shell, else <c>bash</c> where the runner has it, else <c>sh</c>.</summary>
    private static string Shell(ScriptShell? shell, IReadOnlyCollection<string> shells) => shell switch
    {
        ScriptShell.Bash => "bash",
        ScriptShell.Sh => "sh",
        ScriptShell.Pwsh => "pwsh",
        ScriptShell.Cmd => "cmd",
        _ => shells.Count == 0 || shells.Contains("bash", StringComparer.Ordinal) ? "bash" : "sh",
    };

    /// <summary>The node's inputs with every string rendered; numbers, booleans and structure stay.</summary>
    private static string Inputs(JsonElement inputs, SecretReads reads)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(inputs.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("{}").RootElement : inputs, writer, reads);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer, SecretReads reads)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer, reads);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(item, writer, reads);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(reads.Render(element.GetString()!));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
