using AutoCanvas.Core.Secrets;
using AutoCanvas.Protocol.Runner.V1;

namespace AutoCanvas.Runner.Execution;

/// <summary>What a node step works with while it runs.</summary>
/// <param name="Step">The step.</param>
/// <param name="Inputs">Its inputs.</param>
/// <param name="Log">The job's log.</param>
/// <param name="Redactor">Masks the step's secrets in everything it writes.</param>
/// <param name="Stop">Fires when the job is cancelled or the step runs out of time.</param>
internal sealed record NodeContext(Step Step, NodeInputs Inputs, LogBatcher Log, SecretRedactor Redactor, CancellationToken Stop)
{
    public void Write(LogStream stream, string line) => Log.Write((int)Step.Index, stream, line, Redactor);
}
