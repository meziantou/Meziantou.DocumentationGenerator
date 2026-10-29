using System.Collections.Immutable;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>A folder of the content directory that contains at least one page. Its display name comes from <c>_folder.yml</c>.</summary>
internal sealed class ContentFolder : IContentEntry
{
    public const string MetadataFileName = "_folder.yml";

    public required FullPath Path { get; init; }

    /// <summary>Path relative to the content root with '/' separators; empty for the content root.</summary>
    public required string RelativePath { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }

    /// <summary>Output path of the folder relative to the site root, without trailing '/'; empty for the content root.</summary>
    public required string RoutePath { get; init; }

    /// <summary>Pages and sub-folders, sorted by name.</summary>
    public ImmutableArray<IContentEntry> Entries { get; set; } = [];

    public string Name => Path.Name;

    public bool IsRoot => RelativePath.Length == 0;

    /// <summary>The first page of the folder in name order, searching sub-folders.</summary>
    public AuthoredPage? FirstPage => Entries.Select(e => e as AuthoredPage ?? ((ContentFolder)e).FirstPage).FirstOrDefault(p => p is not null);

    public override string ToString() => RelativePath;
}
