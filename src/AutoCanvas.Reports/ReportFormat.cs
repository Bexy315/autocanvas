namespace AutoCanvas.Reports;

/// <summary>
/// File formats a pipeline can publish as a test or coverage report.
/// </summary>
public enum ReportFormat
{
    /// <summary>JUnit XML test results.</summary>
    JUnit,

    /// <summary>Visual Studio TRX test results.</summary>
    Trx,

    /// <summary>Cobertura XML coverage.</summary>
    Cobertura,
}
