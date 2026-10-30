using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AutoCanvas.Reports.Xml;

/// <summary>
/// Reads Cobertura XML (coverlet, coverage.py, istanbul/nyc, JaCoCo converters). Each
/// <c>&lt;class&gt;</c> is read on its own; totals are computed from the lines, because the rates
/// tools write on the root are rounded and sometimes disagree with the details.
/// </summary>
internal static partial class CoberturaReader
{
    public static ReportReadResult<CoverageReport> Read(XmlReader reader)
    {
        var root = SafeXml.MoveToRoot(reader);
        if (!string.Equals(root, "coverage", StringComparison.Ordinal))
        {
            return Failure.Of<CoverageReport>($"This is not a Cobertura report: it starts with <{root}> instead of <coverage>.");
        }

        var packages = new SortedDictionary<string, PackageLines>(StringComparer.Ordinal);
        var current = string.Empty;
        while (!reader.EOF)
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "class" })
            {
                AddClass(SafeXml.ReadElement(reader), Package(packages, current));
                continue;
            }

            if (reader is { NodeType: XmlNodeType.Element, LocalName: "package" })
            {
                current = reader.GetAttribute("name") ?? string.Empty;
            }

            reader.Read();
        }

        return Build(packages);
    }

    private static PackageLines Package(SortedDictionary<string, PackageLines> packages, string name)
    {
        if (!packages.TryGetValue(name, out var package))
        {
            packages[name] = package = new PackageLines();
        }

        return package;
    }

    private static void AddClass(XElement element, PackageLines package)
    {
        var key = (OuterName(SafeXml.Attribute(element, "name")), SafeXml.Attribute(element, "filename"));
        if (!package.Classes.TryGetValue(key, out var lines))
        {
            package.Classes[key] = lines = [];
        }

        // Only the class's own <lines>: the copies under <methods> would count every line twice.
        foreach (var line in SafeXml.Children(element, "lines").SelectMany(l => SafeXml.Children(l, "line")))
        {
            if (!int.TryParse(SafeXml.Attribute(line, "number"), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            {
                continue;
            }

            var hit = long.TryParse(SafeXml.Attribute(line, "hits"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var hits) && hits > 0;
            var (covered, valid) = Branches(SafeXml.Attribute(line, "condition-coverage"));
            lines[number] = lines.TryGetValue(number, out var seen)
                ? new LineInfo(seen.Hit || hit, Math.Max(seen.BranchesCovered, covered), Math.Max(seen.BranchesValid, valid))
                : new LineInfo(hit, covered, valid);
        }
    }

    private static ReportReadResult<CoverageReport> Build(SortedDictionary<string, PackageLines> packages)
    {
        var total = CoverageCounts.Zero;
        var kept = 0;
        var classCount = 0;
        var result = new List<CoveragePackage>();
        foreach (var (name, package) in packages)
        {
            var classes = new List<CoverageClass>();
            var counts = CoverageCounts.Zero;
            foreach (var ((className, file), lines) in package.Classes.OrderBy(c => c.Key.Name, StringComparer.Ordinal).ThenBy(c => c.Key.File, StringComparer.Ordinal))
            {
                if (lines.Count == 0)
                {
                    continue;
                }

                var coverageClass = ToClass(className, file, lines);
                counts = counts.Add(coverageClass.Counts);
                classCount++;
                if (kept < ReportLimits.MaxClasses)
                {
                    classes.Add(coverageClass);
                    kept++;
                }
            }

            total = total.Add(counts);
            if (counts.LinesValid > 0)
            {
                result.Add(new CoveragePackage(name, counts, classes));
            }
        }

        var truncated = classCount > kept;
        IReadOnlyList<string> warnings = truncated
            ? [$"The report has {classCount} classes; only the first {ReportLimits.MaxClasses} are kept in detail. The totals include all of them."]
            : [];
        return new(new CoverageReport(total, result, truncated), null, warnings);
    }

    private static CoverageClass ToClass(string name, string file, Dictionary<int, LineInfo> lines)
    {
        var counts = new CoverageCounts(
            lines.Values.Count(l => l.Hit),
            lines.Count,
            lines.Values.Sum(l => l.BranchesCovered),
            lines.Values.Sum(l => l.BranchesValid));
        return new CoverageClass(name, file, counts, Ranges(lines.Where(l => !l.Value.Hit).Select(l => l.Key).Order()));
    }

    /// <summary>Sorted line numbers as ranges: 3, 4, 5, 9 become 3-5 and 9.</summary>
    internal static List<LineRange> Ranges(IEnumerable<int> sorted)
    {
        var ranges = new List<LineRange>();
        foreach (var line in sorted)
        {
            if (ranges.Count > 0 && ranges[^1].End == line - 1)
            {
                ranges[^1] = ranges[^1] with { End = line };
            }
            else
            {
                ranges.Add(new LineRange(line, line));
            }
        }

        return ranges;
    }

    /// <summary><c>Ns.Foo/&lt;Bar&gt;d__3</c>, <c>Ns.Foo+&lt;&gt;c</c> and nested <c>Ns.Foo/Inner</c> count as <c>Ns.Foo</c>.</summary>
    private static string OuterName(string name)
    {
        var cut = name.IndexOfAny(['/', '+']);
        return cut > 0 ? name[..cut] : name;
    }

    /// <summary>Cobertura's <c>condition-coverage="50% (1/2)"</c> as covered and total branches.</summary>
    private static (int Covered, int Valid) Branches(string conditionCoverage)
    {
        var match = BranchPattern().Match(conditionCoverage);
        if (!match.Success
            || !int.TryParse(match.Groups["covered"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var covered)
            || !int.TryParse(match.Groups["valid"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var valid))
        {
            return (0, 0);
        }

        return (Math.Min(covered, valid), valid);
    }

    [GeneratedRegex(@"\((?<covered>\d{1,9})/(?<valid>\d{1,9})\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BranchPattern();

    private sealed class PackageLines
    {
        public Dictionary<(string Name, string File), Dictionary<int, LineInfo>> Classes { get; } = [];
    }

    private readonly record struct LineInfo(bool Hit, int BranchesCovered, int BranchesValid);
}
