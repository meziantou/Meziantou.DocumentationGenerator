using System.Collections.Immutable;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Configuration;

internal sealed class SiteSettings
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Language { get; init; }
    public required UrlBasePath BasePath { get; init; }

    /// <summary>Absolute origin such as <c>https://example.com</c>, without path. <see langword="null"/> when canonical URLs cannot be generated.</summary>
    public Uri? SiteUrl { get; init; }
    public FullPath? Logo { get; init; }
    public FullPath? Favicon { get; init; }
    public FullPath? SocialImage { get; init; }
    public required RobotsPolicy Robots { get; init; }
    public string? Footer { get; init; }
    public required ImmutableArray<SiteHeaderLink> HeaderLinks { get; init; }
}
