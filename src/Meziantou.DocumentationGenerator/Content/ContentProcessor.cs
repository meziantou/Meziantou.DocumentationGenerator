using System.Collections.Immutable;
using System.Text;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Discovers authored pages, registers their routes, and renders them and the product landing pages.</summary>
internal sealed class ContentProcessor(SiteConfiguration configuration, LinkService links, SiteAssetRegistry assets, DiagnosticBag diagnostics)
{
    public const long MaxMarkdownSize = 4 * 1024 * 1024;

    private readonly MarkdownRenderer _renderer = new(links, new HighlightingService(diagnostics), diagnostics);
    private readonly Dictionary<FullPath, AuthoredPage> _pagesBySource = [];

    public ContentModel Process(IReadOnlyList<DiscoveredPackage> packages, CancellationToken cancellationToken)
    {
        var pages = DiscoverPages(cancellationToken);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = new MarkdownSource(page.DisplayPath, "", LinkContext.None, CreateLocalResolver(page.Source, configuration.Content!.Root, outputPrefix: "", page.DisplayPath));
            page.Rendered = _renderer.Render(page.Document!, page.FrontMatter, source);
            page.Document = null;
        }

        var landingPages = ImmutableDictionary.CreateBuilder<string, RenderedMarkdown>(StringComparer.Ordinal);
        foreach (var product in configuration.Products)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var landing = RenderLandingPage(product, packages);
            if (landing is not null)
            {
                landingPages.Add(product.Id, landing);
            }
        }

        return new ContentModel { Pages = pages, ProductLandingPages = landingPages.ToImmutable() };
    }

    private ImmutableArray<AuthoredPage> DiscoverPages(CancellationToken cancellationToken)
    {
        if (configuration.Content is not { } content)
            return [];

        var excludes = content.Exclude.Select(p => new GlobPattern(p)).ToList();
        var files = Directory.EnumerateFiles(content.Root, "*.md", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint, MatchCasing = MatchCasing.CaseInsensitive })
            .Select(FullPath.FromPath)
            .Select(path => (Path: path, Relative: path.MakePathRelativeTo(content.Root).Replace('\\', '/')))
            .Where(f => !excludes.Any(e => e.IsMatch(f.Relative)))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();

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
            var id = relative[..^".md".Length];
            var routePath = GetRoutePath(id, frontMatter.Slug, location);
            var route = new SiteRoute(routePath, PageKind.Article, "page " + relative);
            if (!links.TryRegister(route, location))
                continue;

            var page = new AuthoredPage
            {
                Source = path,
                Id = id,
                DisplayPath = displayPath,
                Route = route,
                Url = links.Urls.GetUrl(route.FilePath),
                FrontMatter = frontMatter,
                Document = document,
            };

            _pagesBySource.Add(path, page);
            result.Add(page);
        }

        return result.ToImmutable();
    }

    /// <summary><c>guides/intro</c> becomes <c>guides/intro.html</c>; <c>guides/index</c> becomes <c>guides/index.html</c>, linked as <c>guides/</c>.</summary>
    private string GetRoutePath(string id, string? slug, DiagnosticLocation location)
    {
        var value = id;
        if (slug is not null)
        {
            var normalized = slug.Trim().Trim('/');
            if (normalized.Length > 0 && LinkService.IsSafeRelativePath(normalized) && !normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                value = normalized;
            }
            else
            {
                diagnostics.Report(DiagnosticDescriptors.InvalidFrontMatter, $"Invalid slug '{slug}': use a relative path without extension, such as guides/intro", location);
            }
        }

        var segments = value.Split('/').Select(RouteNames.SanitizeFileName).ToList();
        return string.Join('/', segments) + ".html";
    }

    private RenderedMarkdown? RenderLandingPage(ProductConfiguration product, IReadOnlyList<DiscoveredPackage> packages)
    {
        var outputPrefix = product.Id + "/_readme/";
        if (product.LandingPage is { } landingPage)
            return RenderLocalFile(landingPage, outputPrefix, product.Id);

        var readmePackageId = product.ReadmePackage ?? product.Packages[0];
        var candidates = new[] { readmePackageId }.Concat(product.Packages).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in candidates)
        {
            var package = packages.FirstOrDefault(p => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase));
            if (package is null)
                continue;

            if (package.ReadmeOverride is { } readmeOverride)
                return RenderLocalFile(readmeOverride, outputPrefix, product.Id);

            if (package.ReadmeEntry is { } readmeEntry)
                return RenderPackageReadme(package.Package, readmeEntry, outputPrefix, product.Id);
        }

        return null;
    }

    private RenderedMarkdown RenderLocalFile(FullPath path, string outputPrefix, string productId)
    {
        var displayPath = configuration.GetDisplayPath(path);

        // A local README can reference files next to it, but not outside the configuration directory
        var allowedRoot = path.IsChildOf(configuration.BaseDirectory) ? configuration.BaseDirectory : path.Parent;
        var resolver = CreateLocalResolver(path, allowedRoot, outputPrefix, displayPath);
        return _renderer.Render(new MarkdownSource(displayPath, File.ReadAllText(path, Encoding.UTF8), new LinkContext(Scope: null, productId), resolver));
    }

    private RenderedMarkdown RenderPackageReadme(AcquiredPackage package, string readmeEntry, string outputPrefix, string productId)
    {
        using var archive = PackageArchive.Open(package.Path);
        var content = Encoding.UTF8.GetString(archive.ReadAllBytes(readmeEntry, MaxMarkdownSize));
        var resolver = new PackageReadmeLinkResolver(package, readmeEntry, archive.Entries.ToHashSet(StringComparer.Ordinal), outputPrefix, assets, diagnostics);
        return _renderer.Render(new MarkdownSource(package.Configuration.Identity + "/" + readmeEntry, content, new LinkContext(Scope: null, productId), resolver));
    }

    private LocalMarkdownLinkResolver CreateLocalResolver(FullPath file, FullPath allowedRoot, string outputPrefix, string displayPath)
    {
        return new LocalMarkdownLinkResolver(file, allowedRoot, outputPrefix, displayPath, path => _pagesBySource.GetValueOrDefault(path), assets, diagnostics);
    }
}
