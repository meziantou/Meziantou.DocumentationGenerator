using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <param name="Links">Context used to resolve <c>cref</c> attributes: the assembly the documentation comes from and the product of the page.</param>
/// <param name="SourcePath">XML file, for diagnostics.</param>
/// <param name="SymbolId">Documented symbol, for diagnostics.</param>
internal sealed record XmlRenderContext(LinkContext Links, string? SourcePath, string? SymbolId);
