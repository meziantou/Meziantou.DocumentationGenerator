using System.Xml;
using System.Xml.Linq;
using Meziantou.DocumentationGenerator.Diagnostics;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>
/// A compiler XML documentation file. DTDs and external entities are prohibited and the document size is bounded, so a
/// hostile file cannot read local files, reach the network or exhaust memory.
/// </summary>
internal sealed class XmlDocumentationFile
{
    public const long MaxCharacters = 64L * 1024 * 1024;

    private readonly Dictionary<string, XElement> _members;

    private XmlDocumentationFile(string displayPath, Dictionary<string, XElement> members)
    {
        DisplayPath = displayPath;
        _members = members;
    }

    public string DisplayPath { get; }

    public IReadOnlyCollection<string> MemberIds => _members.Keys;

    public static XmlReaderSettings ReaderSettings { get; } = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxCharacters,
        MaxCharactersFromEntities = 1024,
        IgnoreProcessingInstructions = true,
        IgnoreComments = true,
        CloseInput = false,
    };

    public static XmlDocumentationFile? Load(Stream stream, string displayPath, DiagnosticBag diagnostics, DiagnosticLocation location)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(stream, ReaderSettings);
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            diagnostics.Report(DiagnosticDescriptors.MalformedXmlDocumentation, $"'{displayPath}' is not valid XML and is ignored: {ex.Message}", location with { FilePath = displayPath, Line = ex.LineNumber, Column = ex.LinePosition });
            return null;
        }

        var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var member in document.Root?.Element("members")?.Elements("member") ?? [])
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            if (!members.TryAdd(name, member))
            {
                diagnostics.Report(DiagnosticDescriptors.DuplicateXmlDocumentation, $"'{name}' is documented more than once; the first entry is used", location with { FilePath = displayPath, Line = GetLine(member), Symbol = name });
            }
        }

        return new XmlDocumentationFile(displayPath, members);
    }

    public XElement? Find(string documentationId) => _members.GetValueOrDefault(documentationId);

    internal static int? GetLine(XObject node) => node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : null;
}
