using AutoCanvas.Core.Secrets;

namespace AutoCanvas.Server.Application.Reports;

/// <summary>
/// A report file the server takes, from <see cref="ReportIngestion.BeginAsync"/>: either one stored
/// before (the runner uploads it again after a reconnect), or one whose content is to be read with
/// <see cref="ReportIngestion.StoreAsync"/>.
/// </summary>
public sealed class ReportReceipt
{
    internal ReportReceipt(Guid storedId)
    {
        StoredId = storedId;
    }

    internal ReportReceipt(ReportFile file, string name, Guid runId, Guid? automationId, string jobKey, SecretRedactor redactor)
    {
        File = file;
        Name = name;
        RunId = runId;
        AutomationId = automationId;
        JobKey = jobKey;
        Redactor = redactor;
    }

    /// <summary>The id of the report stored before; <see langword="null"/> if the content is still to be read.</summary>
    public Guid? StoredId { get; }

    internal ReportFile? File { get; }

    internal string Name { get; } = string.Empty;

    internal Guid RunId { get; }

    internal Guid? AutomationId { get; }

    internal string JobKey { get; } = string.Empty;

    internal SecretRedactor? Redactor { get; }
}
