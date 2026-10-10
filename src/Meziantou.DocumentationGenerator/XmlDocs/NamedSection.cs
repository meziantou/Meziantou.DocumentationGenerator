using System.Xml.Linq;

namespace Meziantou.DocumentationGenerator.XmlDocs;

internal sealed record NamedSection(string Name, XElement Content);
