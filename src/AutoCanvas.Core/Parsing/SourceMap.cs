using AutoCanvas.Core.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace AutoCanvas.Core.Parsing;

/// <summary>Records the position of every value under the same paths the parser uses in diagnostics.</summary>
internal static class SourceMap
{
    /// <summary>Builds the map; only called after the size check, so the walk is bounded.</summary>
    public static Dictionary<string, SourceLocation> Build(YamlNode root)
    {
        var locations = new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
        Add(root, string.Empty, locations);
        return locations;
    }

    private static void Add(YamlNode node, string path, Dictionary<string, SourceLocation> locations)
    {
        locations.TryAdd(path, new SourceLocation((int)node.Start.Line, (int)node.Start.Column));
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var (key, value) in mapping.Children)
                {
                    if (key is YamlScalarNode { Value: { } name })
                    {
                        Add(value, ParseContext.Child(path, name), locations);
                    }
                }

                break;
            case YamlSequenceNode sequence:
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    Add(sequence.Children[i], ParseContext.Item(path, i), locations);
                }

                break;
        }
    }
}
