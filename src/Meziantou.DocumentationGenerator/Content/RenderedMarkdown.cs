using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Sanitized HTML of a Markdown document and its metadata.</summary>
internal sealed record RenderedMarkdown(string Html, string? Title, string? Description, ImmutableArray<HeadingInfo> Headings);
