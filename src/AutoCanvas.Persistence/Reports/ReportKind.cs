namespace AutoCanvas.Persistence.Reports;

/// <summary>What a report holds.</summary>
public enum ReportKind
{
    /// <summary>Test results (JUnit, TRX).</summary>
    Tests,

    /// <summary>Code coverage (Cobertura).</summary>
    Coverage,
}
