using Meziantou.DocumentationGenerator.Links;
using Meziantou.Framework;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>A Markdown page of the content directory.</summary>
internal sealed class AuthoredPage : IContentEntry
{
    public required FullPath Source { get; init; }

    /// <summary>Path relative to the content root, without extension and with '/' separators, such as <c>03-Guides/01-Overview</c>. It does not depend on the route, which comes from titles and slugs.</summary>
    public required string Id { get; init; }
    public required string DisplayPath { get; init; }
    public required SiteRoute Route { get; init; }
    public required string Url { get; init; }
    public required FrontMatter FrontMatter { get; init; }
    internal MarkdownDocument? Document { get; set; }
    public RenderedMarkdown? Rendered { get; set; }

    /// <summary>Title from the front matter, the first level-1 heading, or the file name without its order prefix.</summary>
    public string Title => Rendered?.Title ?? FrontMatter.Title ?? RouteNames.RemoveOrderPrefix(Source.NameWithoutExtension);

    public string? Description => Rendered?.Description ?? FrontMatter.Description;

    public string Name => Source.Name;
}
