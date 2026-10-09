using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>Documentation shared by several target frameworks. Several variants are displayed when the documentation differs.</summary>
internal sealed record DocumentationVariant(ImmutableArray<string> TargetFrameworks, DocumentationComment Comment);
