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

    /// <summary>Pages and sub-folders, sorted by sort order, then by name.</summary>
    public ImmutableArray<IContentEntry> Entries { get; set; } = [];

    public string Name => Path.Name;

    /// <summary>The <c>sortOrder</c> of <c>_folder.yml</c>. Default: 0.</summary>
    public int SortOrder { get; init; }

    public bool IsRoot => RelativePath.Length == 0;

    /// <summary>The first page of the folder in entry order, searching sub-folders.</summary>
    public AuthoredPage? FirstPage => Entries.Select(e => e as AuthoredPage ?? ((ContentFolder)e).FirstPage).FirstOrDefault(p => p is not null);

    /// <summary>
    /// Pages and sub-folders of this folder, in entry order, as listed by the generated home page and
    /// <c>&lt;SectionList /&gt;</c>. Sub-folders link to their first page; sub-folders without page and the home page
    /// (the root <c>index.md</c>) are skipped.
    /// </summary>
    public IReadOnlyList<EntryListItem> GetSectionItems()
    {
        var result = new List<EntryListItem>();
        foreach (var entry in Entries)
        {
            switch (entry)
            {
                case AuthoredPage page when page.Route.FilePath != Links.LinkService.HomePath:
                    result.Add(new EntryListItem(page.Title, page.Url, page.Description));
                    break;
                case ContentFolder { FirstPage: { } first } folder:
                    result.Add(new EntryListItem(folder.Title, first.Url, folder.Description));
                    break;
            }
        }

        return result;
    }

    public override string ToString() => RelativePath;
}
