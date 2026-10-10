using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Content;

internal sealed class ContentModel
{
    public static ContentModel Empty { get; } = new() { Pages = [], ProductLandingPages = ImmutableDictionary<string, RenderedMarkdown>.Empty };

    /// <summary>Authored pages sorted by identifier.</summary>
    public required ImmutableArray<AuthoredPage> Pages { get; init; }

    /// <summary>Rendered landing page of each product that has a README or a configured landing page.</summary>
    public required ImmutableDictionary<string, RenderedMarkdown> ProductLandingPages { get; init; }

    /// <summary>The authored page used as the home page of the site (<c>index.md</c> at the root of the content directory).</summary>
    public AuthoredPage? HomePage => Pages.FirstOrDefault(p => p.Route.FilePath == Links.LinkService.HomePath);

    public AuthoredPage? FindByUid(string uid) => Pages.FirstOrDefault(p => string.Equals(p.FrontMatter.Uid, uid, StringComparison.Ordinal));
}
