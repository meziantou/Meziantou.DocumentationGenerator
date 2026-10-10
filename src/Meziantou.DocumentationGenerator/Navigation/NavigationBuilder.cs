using System.Collections.Immutable;
using System.Text;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.Framework;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.DocumentationGenerator.Navigation;

/// <summary>
/// Builds the navigation from the DocFX-style <c>toc.yml</c> subset documented in docs/architecture.md and from the
/// aggregated API model. Relative paths are resolved against the TOC that declares them.
/// </summary>
internal sealed class NavigationBuilder(SiteConfiguration configuration, ContentModel content, ApiCatalog catalog, LinkService links, DiagnosticBag diagnostics)
{
    private const int MaxIncludeDepth = 16;
    private const string ApiPrefix = "api:";
    private static readonly HashSet<string> SupportedKeys = new(StringComparer.Ordinal) { "name", "href", "uid", "items", "expanded", "topicHref", "topicUid" };

    private readonly Dictionary<FullPath, AuthoredPage> _pagesBySource = content.Pages.ToDictionary(p => p.Source);

    public SiteNavigation Build()
    {
        var products = configuration.Products.ToImmutableDictionary(p => p.Id, CreateProductTree, StringComparer.Ordinal);
        ImmutableArray<NavigationNode> root;
        if (configuration.Content?.Toc is { } tocPath)
        {
            root = LoadToc(tocPath, [], products);
            foreach (var page in content.Pages)
            {
                if (!root.SelectMany(n => n.DescendantsAndSelf()).Any(n => n.PagePath == page.Route.FilePath))
                {
                    diagnostics.Report(DiagnosticDescriptors.PageNotInToc, $"'{page.DisplayPath}' is not referenced by the table of contents; it is generated and can be found with search", new DiagnosticLocation { FilePath = page.DisplayPath });
                }
            }
        }
        else
        {
            root = CreateDefaultTree();
        }

        var header = ImmutableArray.CreateBuilder<NavigationNode>();
        foreach (var product in configuration.Products)
        {
            header.Add(new NavigationNode { Title = product.Name, Url = links.GetProductUrl(product.Id), PagePath = RouteNames.GetProductPath(product.Id) });
        }

        var homeTitle = content.HomePage?.Title ?? configuration.Site.Title;
        return new SiteNavigation(root, header.ToImmutable(), products, homeTitle, links.HomeUrl);
    }

    /// <summary>Without a table of contents: the authored pages in path order, then the products.</summary>
    private ImmutableArray<NavigationNode> CreateDefaultTree()
    {
        var nodes = ImmutableArray.CreateBuilder<NavigationNode>();
        foreach (var page in content.Pages.OrderBy(p => p.Id == "index" ? "" : p.Id, StringComparer.Ordinal))
        {
            nodes.Add(new NavigationNode { Title = page.Title, Url = page.Url, PagePath = page.Route.FilePath });
        }

        foreach (var product in configuration.Products)
        {
            nodes.Add(new NavigationNode { Title = product.Name, Url = links.GetProductUrl(product.Id), PagePath = RouteNames.GetProductPath(product.Id) });
        }

        return nodes.ToImmutable();
    }

    private ImmutableArray<NavigationNode> CreateProductTree(ProductConfiguration product)
    {
        var nodes = ImmutableArray.CreateBuilder<NavigationNode>();
        nodes.Add(new NavigationNode { Title = "Overview", Url = links.GetProductUrl(product.Id), PagePath = RouteNames.GetProductPath(product.Id) });
        var api = CreateApiNodes(product.Id);
        if (!api.IsEmpty)
        {
            nodes.Add(new NavigationNode { Title = "API reference", Url = links.GetApiReferenceUrl(product.Id), PagePath = RouteNames.GetApiReferencePath(product.Id), Children = api, IsExpanded = true });
        }

        return nodes.ToImmutable();
    }

    /// <summary>Namespaces sorted ordinally, each listing its types (including nested types) sorted by metadata name.</summary>
    private ImmutableArray<NavigationNode> CreateApiNodes(string productId)
    {
        var product = catalog.Products.FirstOrDefault(p => p.Product.Id == productId);
        if (product is null)
            return [];

        return [.. product.Namespaces.Select(ns => new NavigationNode
        {
            Title = ns.IsGlobal ? "Global namespace" : ns.Name,
            Url = links.GetNamespaceUrl(productId, ns.Name),
            PagePath = RouteNames.GetNamespacePath(productId, ns.Name),
            Kind = "namespace",
            Children = [.. ns.Types.Select(type => new NavigationNode
            {
                Title = type.DisplayName,
                Url = links.GetUrl(type),
                PagePath = links.GetTypeRoute(type)?.FilePath,
                Kind = "type",
            })],
        })];
    }

    private ImmutableArray<NavigationNode> LoadToc(FullPath path, List<FullPath> stack, ImmutableDictionary<string, ImmutableArray<NavigationNode>> products)
    {
        var displayPath = configuration.GetDisplayPath(path);
        if (stack.Contains(path))
        {
            diagnostics.Report(DiagnosticDescriptors.TocCycle, $"Tables of contents include each other: {string.Join(" -> ", stack.Append(path).Select(configuration.GetDisplayPath))}", new DiagnosticLocation { FilePath = displayPath });
            return [];
        }

        if (stack.Count >= MaxIncludeDepth)
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidToc, "Tables of contents are nested too deeply", new DiagnosticLocation { FilePath = displayPath });
            return [];
        }

        var root = YamlDocumentLoader.Load(File.ReadAllText(path, Encoding.UTF8), displayPath, diagnostics, DiagnosticDescriptors.InvalidToc);
        var items = root switch
        {
            YamlSequence sequence => sequence,
            YamlMapping mapping when mapping.TryGetValue("items", out var value) && value is YamlSequence sequence => sequence,
            null => null,
            _ => null,
        };

        if (items is null)
        {
            if (root is not null)
            {
                diagnostics.Report(DiagnosticDescriptors.InvalidToc, "A table of contents must be a sequence of items or a mapping with an 'items' sequence", Location(displayPath, root));
            }

            return [];
        }

        stack.Add(path);
        try
        {
            return ParseItems(items, path, displayPath, stack, products);
        }
        finally
        {
            stack.RemoveAt(stack.Count - 1);
        }
    }

    private ImmutableArray<NavigationNode> ParseItems(YamlSequence items, FullPath tocPath, string displayPath, List<FullPath> stack, ImmutableDictionary<string, ImmutableArray<NavigationNode>> products)
    {
        var result = ImmutableArray.CreateBuilder<NavigationNode>();
        foreach (var item in items)
        {
            if (item is not YamlMapping mapping)
            {
                diagnostics.Report(DiagnosticDescriptors.InvalidToc, "A table of contents item must be a mapping", Location(displayPath, item));
                continue;
            }

            foreach (var key in mapping.Keys.OfType<YamlValue>().Select(k => k.Value).Where(k => !SupportedKeys.Contains(k)))
            {
                diagnostics.Report(DiagnosticDescriptors.UnsupportedTocKey, $"The key '{key}' is not supported and is ignored", Location(displayPath, mapping));
            }

            result.AddRange(ParseItem(mapping, tocPath, displayPath, stack, products));
        }

        return result.ToImmutable();
    }

    private ImmutableArray<NavigationNode> ParseItem(YamlMapping mapping, FullPath tocPath, string displayPath, List<FullPath> stack, ImmutableDictionary<string, ImmutableArray<NavigationNode>> products)
    {
        var location = Location(displayPath, mapping);
        var name = YamlDocumentLoader.GetString(mapping, "name");
        var href = YamlDocumentLoader.GetString(mapping, "href") ?? YamlDocumentLoader.GetString(mapping, "topicHref");
        var uid = YamlDocumentLoader.GetString(mapping, "uid") ?? YamlDocumentLoader.GetString(mapping, "topicUid");
        var expanded = YamlDocumentLoader.GetString(mapping, "expanded") is { } expandedValue && bool.TryParse(expandedValue, out var isExpanded) && isExpanded;
        var children = mapping.TryGetValue("items", out var itemsValue) && itemsValue is YamlSequence childItems
            ? ParseItems(childItems, tocPath, displayPath, stack, products)
            : [];

        // api:{productId} inserts the generated API navigation of a product
        if (href is not null && href.StartsWith(ApiPrefix, StringComparison.Ordinal))
        {
            var productId = href[ApiPrefix.Length..];
            if (!products.ContainsKey(productId))
            {
                diagnostics.Report(DiagnosticDescriptors.TocTargetNotFound, $"Unknown product '{productId}'", location);
                return [];
            }

            var api = CreateApiNodes(productId);
            if (name is null)
                return api;

            return [new NavigationNode { Title = name, Url = links.GetApiReferenceUrl(productId), PagePath = RouteNames.GetApiReferencePath(productId), Children = [.. api, .. children], IsExpanded = expanded }];
        }

        if (href is not null && Uri.TryCreate(href, UriKind.Absolute, out var absolute) && !href.StartsWith('/', StringComparison.Ordinal))
        {
            if (absolute.Scheme is not ("http" or "https"))
            {
                diagnostics.Report(DiagnosticDescriptors.InvalidToc, $"The link '{href}' must use http or https", location);
                return [];
            }

            return [new NavigationNode { Title = name ?? href, Url = href, IsExternal = true, Children = children, IsExpanded = expanded }];
        }

        if (href is not null && (href.EndsWith("toc.yml", StringComparison.OrdinalIgnoreCase) || href.EndsWith('/', StringComparison.Ordinal)))
        {
            var target = FullPath.Combine(tocPath.Parent, href.EndsWith('/', StringComparison.Ordinal) ? href + "toc.yml" : href);
            if (!IsInContentRoot(target, location))
                return [];

            if (!File.Exists(target))
            {
                diagnostics.Report(DiagnosticDescriptors.TocTargetNotFound, $"'{href}' not found", location);
                return [];
            }

            var included = LoadToc(target, stack, products);
            if (name is null)
                return [.. included, .. children];

            // A group referencing a table of contents links to its first article
            var first = included.SelectMany(n => n.DescendantsAndSelf()).FirstOrDefault(n => n.Url is not null && !n.IsExternal);
            return [new NavigationNode { Title = name, Url = first?.Url, PagePath = null, Children = [.. included, .. children], IsExpanded = expanded }];
        }

        if (href is not null)
        {
            var (path, fragment) = SplitFragment(href);
            var target = FullPath.Combine(tocPath.Parent, Uri.UnescapeDataString(path));
            if (!IsInContentRoot(target, location))
                return [];

            if (!_pagesBySource.TryGetValue(target, out var page))
            {
                diagnostics.Report(DiagnosticDescriptors.TocTargetNotFound, $"'{href}' is not a page of the content directory", location);
                return [];
            }

            var url = fragment is null ? page.Url : page.Url + "#" + UrlBuilder.EncodeFragment(fragment);
            return [new NavigationNode { Title = name ?? page.Title, Url = url, PagePath = fragment is null ? page.Route.FilePath : null, Children = children, IsExpanded = expanded }];
        }

        if (uid is not null)
        {
            var page = content.FindByUid(uid);
            if (page is not null)
                return [new NavigationNode { Title = name ?? page.Title, Url = page.Url, PagePath = page.Route.FilePath, Children = children, IsExpanded = expanded }];

            var resolution = links.Resolve(uid, LinkContext.None);
            if (resolution.Kind is CrefResolutionKind.Resolved && resolution.Url is not null)
            {
                var pagePath = resolution.Symbol is ApiTypeModel type ? links.GetTypeRoute(type)?.FilePath : null;
                return [new NavigationNode { Title = name ?? resolution.DisplayName, Url = resolution.Url, PagePath = pagePath, Children = children, IsExpanded = expanded }];
            }

            diagnostics.Report(DiagnosticDescriptors.TocTargetNotFound, resolution.Kind is CrefResolutionKind.Ambiguous ? $"The uid '{uid}' is ambiguous: {string.Join(", ", resolution.Candidates)}" : $"No page or API symbol has the uid '{uid}'", location);
            return [];
        }

        if (name is null)
        {
            diagnostics.Report(DiagnosticDescriptors.InvalidToc, "A table of contents item requires a name, an href or a uid", location);
            return children;
        }

        // A group without link is not clickable
        return [new NavigationNode { Title = name, Children = children, IsExpanded = expanded }];
    }

    private bool IsInContentRoot(FullPath target, DiagnosticLocation location)
    {
        if (configuration.Content is { } contentConfiguration && (target.IsChildOf(contentConfiguration.Root) || target == contentConfiguration.Root))
            return true;

        diagnostics.Report(DiagnosticDescriptors.PathEscapesRoot, $"'{target.Name}' is outside the content directory", location);
        return false;
    }

    private static (string Path, string? Fragment) SplitFragment(string href)
    {
        var index = href.IndexOf('#', StringComparison.Ordinal);
        return index < 0 ? (href, null) : (href[..index], href[(index + 1)..]);
    }

    private static DiagnosticLocation Location(string displayPath, YamlElement element)
    {
        var location = YamlDocumentLoader.GetLocation(element);
        return new DiagnosticLocation { FilePath = displayPath, Line = location?.Line, Column = location?.Column };
    }
}
