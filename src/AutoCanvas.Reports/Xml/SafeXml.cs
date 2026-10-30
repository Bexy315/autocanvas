using System.Xml;
using System.Xml.Linq;

namespace AutoCanvas.Reports.Xml;

/// <summary>
/// Reading untrusted report XML: no DTDs (so no external entities and no entity expansion), no
/// resolver, and element names matched by local name because writers differ in namespaces.
/// </summary>
internal static class SafeXml
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = false,
    };

    /// <summary>A reader over <paramref name="content"/>; the stream is read as bytes so a byte order mark is honored.</summary>
    public static XmlReader Open(Stream content) => XmlReader.Create(content, Settings);

    /// <summary>Moves to the root element and returns its local name, or <see langword="null"/> for a document without one.</summary>
    public static string? MoveToRoot(XmlReader reader) =>
        reader.MoveToContent() == XmlNodeType.Element ? reader.LocalName : null;

    /// <summary>Reads the element the reader is on, with its children, and leaves the reader after it.</summary>
    public static XElement ReadElement(XmlReader reader) => (XElement)XNode.ReadFrom(reader);

    public static string Attribute(XElement element, string name) => (string?)element.Attribute(name) ?? string.Empty;

    public static IEnumerable<XElement> Children(XElement element, string localName) =>
        element.Elements().Where(child => string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal));

    public static XElement? Child(XElement element, string localName) => Children(element, localName).FirstOrDefault();

    /// <summary>A user-facing message for a reader that hit malformed XML.</summary>
    public static string Describe(XmlException exception, string format) =>
        $"The {format} report is not valid XML (line {exception.LineNumber}, position {exception.LinePosition}): {exception.Message}";
}
