using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Content;

internal sealed class ContentModel
{
    public static ContentModel Empty { get; } = new() { Pages = [], Root = null };

    /// <summary>Authored pages sorted by path.</summary>
    public required ImmutableArray<AuthoredPage> Pages { get; init; }

    /// <summary>The content directory, or <see langword="null"/> when no content is configured.</summary>
    public required ContentFolder? Root { get; init; }

    /// <summary>The authored page used as the home page of the site (<c>index.md</c> at the root of the content directory).</summary>
    public AuthoredPage? HomePage => Pages.FirstOrDefault(p => p.Route.FilePath == Links.LinkService.HomePath);
}
