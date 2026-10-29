using System.Collections.Immutable;
using System.Text;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.Framework;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>
/// Discovers the authored pages and their folders, registers their routes, and renders them. Pages are only the Markdown
/// files of the content directory: nothing is generated from packages.
/// </summary>
internal sealed class ContentProcessor(SiteConfiguration configuration, LinkService links, SiteAssetRegistry assets, DiagnosticBag diagnostics)
{
    public const long MaxMarkdownSize = 4 * 1024 * 1024;

    private readonly MarkdownRenderer _renderer = new(links, new HighlightingService(diagnostics), diagnostics);
    private readonly Dictionary<FullPath, AuthoredPage> _pagesBySource = [];
    private readonly Dictionary<FullPath, ContentFolder> _foldersByPath = [];

    public ContentModel Process(CancellationToken cancellationToken)
    {
        if (configuration.Content is not { } content)
            return ContentModel.Empty;

        var (pages, root) = DiscoverPages(content, cancellationToken);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = new MarkdownSource(page.DisplayPath, "", LinkContext.None, CreateLocalResolver(page.Source, content.Root, page.DisplayPath));
            page.Rendered = _renderer.Render(page.Document!, page.FrontMatter, source);
            page.Document = null;
        }

        return new ContentModel { Pages = pages, Root = root };
    }

    private (ImmutableArray<AuthoredPage> Pages, ContentFolder Root) DiscoverPages(ContentConfiguration content, CancellationToken cancellationToken)
    {
        var excludes = content.Exclude.Select(p => new GlobPattern(p)).ToList();
        var files = Directory.EnumerateFiles(content.Root, "*.md", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint, MatchCasing = MatchCasing.CaseInsensitive })
            .Select(FullPath.FromPath)
            .Select(path => (Path: path, Relative: path.MakePathRelativeTo(content.Root).Replace('\\', '/')))
            .Where(f => !excludes.Any(e => e.IsMatch(f.Relative)))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();

        var root = new ContentFolder { Path = content.Root, RelativePath = "", Title = configuration.Site.Title, RoutePath = "" };
        _foldersByPath.Add(content.Root, root);
        var entries = new Dictionary<ContentFolder, List<IContentEntry>> { [root] = [] };

        var result = ImmutableArray.CreateBuilder<AuthoredPage>();
        foreach (var (path, relative) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displayPath = configuration.GetDisplayPath(path);
            var location = new DiagnosticLocation { FilePath = displayPath };
            if (new FileInfo(path).Length > MaxMarkdownSize)
            {
                diagnostics.Report(DiagnosticDescriptors.InvalidFrontMatter, $"'{displayPath}' is larger than {MaxMarkdownSize} bytes and is ignored", location);
                continue;
            }

            var text = File.ReadAllText(path, Encoding.UTF8);
            var (document, frontMatter) = MarkdownRenderer.Parse(text, displayPath, diagnostics);
            var folder = GetFolder(path.Parent, content.Root, entries);
            var title = MarkdownRenderer.GetTitle(document, frontMatter) ?? RouteNames.RemoveOrderPrefix(path.NameWithoutExtension);
            var routePath = relative.Equals("index.md", StringComparison.OrdinalIgnoreCase)
                ? LinkService.HomePath
                : CombineRoute(folder.RoutePath, GetSegment(frontMatter.Slug, title, path.NameWithoutExtension, location, DiagnosticDescriptors.InvalidFrontMatter)) + ".html";
            var route = new SiteRoute(routePath, PageKind.Article, "page " + relative);
            if (!links.TryRegister(route, location))
                continue;

            var page = new AuthoredPage
            {
                Source = path,
                Id = relative[..^".md".Length],
                DisplayPath = displayPath,
                Route = route,
                Url = links.Urls.GetUrl(route.FilePath),
                FrontMatter = frontMatter,
                Document = document,
            };

            _pagesBySource.Add(path, page);
            entries[folder].Add(page);
            result.Add(page);
        }

        foreach (var (folder, items) in entries)
        {
            folder.Entries = [.. items.OrderBy(e => e.Name, StringComparer.Ordinal)];
        }

        return (result.ToImmutable(), root);
    }

    /// <summary>Returns the folder of a page, creating it and its parents. Only folders containing pages are created.</summary>
    private ContentFolder GetFolder(FullPath path, FullPath contentRoot, Dictionary<ContentFolder, List<IContentEntry>> entries)
    {
        if (_foldersByPath.TryGetValue(path, out var existing))
            return existing;

        var parent = GetFolder(path.Parent, contentRoot, entries);
        var relativePath = path.MakePathRelativeTo(contentRoot).Replace('\\', '/');
        var metadataPath = path / ContentFolder.MetadataFileName;
        var displayPath = configuration.GetDisplayPath(metadataPath);
        var location = new DiagnosticLocation { FilePath = displayPath };
        var (title, description, slug) = LoadFolderMetadata(metadataPath, displayPath, location);
        title ??= RouteNames.RemoveOrderPrefix(path.Name);

        var folder = new ContentFolder
        {
            Path = path,
            RelativePath = relativePath,
            Title = title,
            Description = description,
            RoutePath = CombineRoute(parent.RoutePath, GetSegment(slug, title, path.Name, location, DiagnosticDescriptors.InvalidFolderMetadata)),
        };

        _foldersByPath.Add(path, folder);
        entries.Add(folder, []);
        entries[parent].Add(folder);
        return folder;
    }

    private (string? Title, string? Description, string? Slug) LoadFolderMetadata(FullPath path, string displayPath, DiagnosticLocation location)
    {
        if (!File.Exists(path))
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidFolderMetadata, $"'{displayPath}' not found; the folder name is used as title", location);
            return (null, null, null);
        }

        if (new FileInfo(path).Length > YamlDocumentLoader.MaxSize)
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidFolderMetadata, $"'{displayPath}' is larger than {YamlDocumentLoader.MaxSize} bytes and is ignored", location);
            return (null, null, null);
        }

        var element = YamlDocumentLoader.Load(File.ReadAllText(path, Encoding.UTF8), displayPath, diagnostics, DiagnosticDescriptors.InvalidFolderMetadata);
        if (element is not YamlMapping mapping)
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidFolderMetadata, "The folder metadata must be a YAML mapping with a 'title'", location);
            return (null, null, null);
        }

        var title = YamlDocumentLoader.GetString(mapping, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidFolderMetadata, "The folder metadata has no 'title'; the folder name is used as title", location);
            title = null;
        }

        return (title, YamlDocumentLoader.GetString(mapping, "description"), YamlDocumentLoader.GetString(mapping, "slug"));
    }

    /// <summary>URL segment of a page or folder: the slug when valid, otherwise computed from the title, then from the file name.</summary>
    private string GetSegment(string? slug, string title, string fileName, DiagnosticLocation location, DiagnosticDescriptor descriptor)
    {
        if (slug is not null)
        {
            var normalized = slug.Trim();
            if (normalized.Length > 0 && !normalized.Contains('/', StringComparison.Ordinal) && LinkService.IsSafeRelativePath(normalized) && !normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && RouteNames.SanitizeFileName(normalized) == normalized)
                return normalized;

            diagnostics.Report(descriptor, $"Invalid slug '{slug}': use a single URL segment without extension, such as getting-started", location);
        }

        var segment = RouteNames.Slugify(title);
        if (segment.Length == 0)
        {
            segment = RouteNames.Slugify(RouteNames.RemoveOrderPrefix(fileName));
        }

        return segment.Length > 0 ? segment : RouteNames.SanitizeFileName(fileName);
    }

    private static string CombineRoute(string parent, string segment) => parent.Length == 0 ? segment : parent + "/" + segment;

    private LocalMarkdownLinkResolver CreateLocalResolver(FullPath file, FullPath allowedRoot, string displayPath)
    {
        return new LocalMarkdownLinkResolver(file, allowedRoot, displayPath, path => _pagesBySource.GetValueOrDefault(path), GetFolderUrl, GetAssetOutputPath, assets, diagnostics);
    }

    /// <summary>
    /// Output path of a linked file: the route of the nearest folder containing pages, followed by the rest of the source
    /// path. <c>01-Imaging/07-Drawing/images/logo.png</c> becomes <c>imaging/drawing/images/logo.png</c>.
    /// </summary>
    private string GetAssetOutputPath(FullPath file)
    {
        var remainder = new List<string> { file.Name };
        var directory = file.Parent;
        ContentFolder? folder;
        while (!_foldersByPath.TryGetValue(directory, out folder))
        {
            remainder.Insert(0, directory.Name);
            directory = directory.Parent;
        }

        return CombineRoute(folder.RoutePath, string.Join('/', remainder));
    }

    /// <summary>A link to a folder targets its first page, or the home page for the content root.</summary>
    private string? GetFolderUrl(FullPath path)
    {
        if (!_foldersByPath.TryGetValue(path, out var folder))
            return null;

        return folder.IsRoot ? links.HomeUrl : folder.FirstPage?.Url;
    }
}
