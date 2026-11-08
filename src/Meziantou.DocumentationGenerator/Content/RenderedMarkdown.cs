using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Sanitized HTML of a Markdown document and its metadata.</summary>
internal sealed record RenderedMarkdown(string Html, string? Title, string? Description, ImmutableArray<HeadingInfo> Headings)
{
    /// <summary>
    /// <c>&lt;SectionList /&gt;</c> elements still to render: each placeholder paragraph of <see cref="Html"/> is replaced
    /// once every page is rendered, because the list shows the title and description of other pages.
    /// </summary>
    public ImmutableArray<(string Placeholder, EntryListLayout Layout)> PendingSectionLists { get; init; } = [];
}
