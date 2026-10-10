using Meziantou.DocumentationGenerator.Links;
using Meziantou.Framework;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>A Markdown page of the content directory.</summary>
internal sealed class AuthoredPage
{
    public required FullPath Source { get; init; }

    /// <summary>Path relative to the content root, without extension and with '/' separators, such as <c>guides/getting-started</c>.</summary>
    public required string Id { get; init; }
    public required string DisplayPath { get; init; }
    public required SiteRoute Route { get; init; }
    public required string Url { get; init; }
    public required FrontMatter FrontMatter { get; init; }
    internal MarkdownDocument? Document { get; set; }
    public RenderedMarkdown? Rendered { get; set; }

    /// <summary>Title from the front matter, the first level-1 heading, or the file name.</summary>
    public string Title => Rendered?.Title ?? FrontMatter.Title ?? Source.NameWithoutExtension;
}
