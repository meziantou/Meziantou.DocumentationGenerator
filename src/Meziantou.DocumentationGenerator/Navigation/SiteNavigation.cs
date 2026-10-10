using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Navigation;

/// <summary>Navigation trees of the site: the root table of contents, the header links, and the generated tree of each product.</summary>
internal sealed class SiteNavigation
{
    public static SiteNavigation Empty { get; } = new([], [], ImmutableDictionary<string, ImmutableArray<NavigationNode>>.Empty, "Home", "/");

    private readonly ImmutableArray<NavigationNode> _articleOrder;

    public SiteNavigation(ImmutableArray<NavigationNode> root, ImmutableArray<NavigationNode> header, ImmutableDictionary<string, ImmutableArray<NavigationNode>> products, string homeTitle, string homeUrl)
    {
        Root = root;
        Header = header;
        Products = products;
        HomeTitle = homeTitle;
        HomeUrl = homeUrl;
        _articleOrder = [.. root.SelectMany(n => n.DescendantsAndSelf()).Where(n => n.PagePath is not null && n.Kind is null).DistinctBy(n => n.PagePath, StringComparer.Ordinal)];
    }

    /// <summary>The tree of the root table of contents, with generated API navigation inserted at <c>api:</c> entries.</summary>
    public ImmutableArray<NavigationNode> Root { get; }

    /// <summary>Links of the site header.</summary>
    public ImmutableArray<NavigationNode> Header { get; }

    /// <summary>Generated navigation of each product: overview, API reference, namespaces and types.</summary>
    public ImmutableDictionary<string, ImmutableArray<NavigationNode>> Products { get; }
    public string HomeTitle { get; }
    public string HomeUrl { get; }

    /// <summary>
    /// Returns the navigation of a page. The root table of contents is used when it contains the page (first occurrence in
    /// depth-first order); otherwise the generated tree of the product of the page is used.
    /// </summary>
    public PageNavigation GetPageNavigation(string pagePath, string? productId)
    {
        var trail = FindTrail(Root, pagePath);
        var sidebar = Root;
        if (trail is null && productId is not null && Products.TryGetValue(productId, out var productTree))
        {
            sidebar = productTree;
            trail = FindTrail(productTree, pagePath);
        }

        trail ??= [];
        var home = new NavigationNode { Title = HomeTitle, Url = HomeUrl, PagePath = LinkService.HomePath };
        var breadcrumbs = ImmutableArray.CreateBuilder<NavigationNode>();
        if (pagePath != LinkService.HomePath)
        {
            breadcrumbs.Add(home);
        }

        breadcrumbs.AddRange(trail.Take(trail.Count - 1).Where(n => n.PagePath != LinkService.HomePath));

        NavigationNode? previous = null;
        NavigationNode? next = null;
        var index = _articleOrder.Select((n, i) => (n, i)).FirstOrDefault(x => x.n.PagePath == pagePath, (null!, -1)).Item2;
        if (index >= 0)
        {
            previous = index > 0 ? _articleOrder[index - 1] : null;
            next = index < _articleOrder.Length - 1 ? _articleOrder[index + 1] : null;
        }

        return new PageNavigation(sidebar, [.. trail], breadcrumbs.ToImmutable(), previous, next);
    }

    public bool Contains(string pagePath) => FindTrail(Root, pagePath) is not null;

    private static List<NavigationNode>? FindTrail(ImmutableArray<NavigationNode> nodes, string pagePath)
    {
        foreach (var node in nodes)
        {
            if (node.PagePath == pagePath)
                return [node];

            var trail = FindTrail(node.Children, pagePath);
            if (trail is not null)
            {
                trail.Insert(0, node);
                return trail;
            }
        }

        return null;
    }
}
