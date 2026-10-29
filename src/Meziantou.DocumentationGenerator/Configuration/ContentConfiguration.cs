using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Configuration;

internal sealed class ContentConfiguration
{
    public const string EditUrlPathPlaceholder = "{path}";

    public required FullPath Root { get; init; }
    public required ImmutableArray<string> Assets { get; init; }
    public required ImmutableArray<string> Exclude { get; init; }

    /// <summary>Absolute URL containing <see cref="EditUrlPathPlaceholder"/>, or <see langword="null"/> when pages have no edit link.</summary>
    public string? EditUrl { get; init; }

    /// <summary>URL to edit the Markdown file <paramref name="source"/>, or <see langword="null"/> when no edit URL is configured.</summary>
    public string? GetEditUrl(FullPath source)
    {
        if (EditUrl is null)
            return null;

        var path = source.MakePathRelativeTo(Root).Replace('\\', '/');
        return EditUrl.Replace(EditUrlPathPlaceholder, UrlBuilder.EncodePath(path), StringComparison.Ordinal);
    }
}
