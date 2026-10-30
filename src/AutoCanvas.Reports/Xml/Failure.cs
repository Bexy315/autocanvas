namespace AutoCanvas.Reports.Xml;

/// <summary>Builds failed read results.</summary>
internal static class Failure
{
    public static ReportReadResult<T> Of<T>(string error)
        where T : class => new(null, error, []);
}
