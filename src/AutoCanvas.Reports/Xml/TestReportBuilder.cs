namespace AutoCanvas.Reports.Xml;

/// <summary>Collects test results up to <see cref="ReportLimits.MaxCases"/> while counting all of them.</summary>
internal sealed class TestReportBuilder
{
    private readonly List<TestCaseResult> _cases = [];
    private int _total;
    private int _passed;
    private int _failed;
    private int _skipped;
    private TimeSpan _duration;

    public void Add(TestCaseResult result)
    {
        _total++;
        _duration += result.Duration;
        switch (result.Outcome)
        {
            case TestOutcome.Passed:
                _passed++;
                break;
            case TestOutcome.Failed:
                _failed++;
                break;
            default:
                _skipped++;
                break;
        }

        if (_cases.Count < ReportLimits.MaxCases)
        {
            _cases.Add(Limited(result));
        }
    }

    public ReportReadResult<TestReport> Build()
    {
        var truncated = _total > _cases.Count;
        IReadOnlyList<string> warnings = truncated
            ? [$"The report has {_total} tests; only the first {ReportLimits.MaxCases} are kept in detail. The counts include all of them."]
            : [];
        return new(new TestReport(_cases, new TestCounts(_total, _passed, _failed, _skipped, _duration), truncated), null, warnings);
    }

    private static TestCaseResult Limited(TestCaseResult result) => result with
    {
        Group = Text.Cut(result.Group, ReportLimits.MaxName),
        Suite = Text.Cut(result.Suite, ReportLimits.MaxName)!,
        Name = Text.Cut(result.Name, ReportLimits.MaxName)!,
        Message = Text.Cut(result.Message, ReportLimits.MaxMessage),
        StackTrace = Text.Cut(result.StackTrace, ReportLimits.MaxStackTrace),
        Output = Text.Cut(result.Output, ReportLimits.MaxOutput),
    };
}
