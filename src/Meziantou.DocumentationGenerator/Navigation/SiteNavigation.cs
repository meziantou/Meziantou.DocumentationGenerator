using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Navigation;

/// <summary>Navigation tree of the site: the authored pages by folder, then the generated API reference.</summary>
internal sealed class SiteNavigation
{
    public static SiteNavigation Empty { get; } = new([], "Home", "/");

    private readonly ImmutableArray<NavigationNode> _articleOrder;
    private readonly Dictionary<NavigationNode, int> _collapsibleNodeIds = new(ReferenceEqualityComparer.Instance);

    public SiteNavigation(ImmutableArray<NavigationNode> root, string homeTitle, string homeUrl)
    {
        Root = root;
        HomeTitle = homeTitle;
        HomeUrl = homeUrl;
        _articleOrder = [.. root.SelectMany(n => n.DescendantsAndSelf()).Where(n => n.PagePath is not null && n.Kind is null).DistinctBy(n => n.PagePath, StringComparer.Ordinal)];

        var collapsibleNodes = ImmutableArray.CreateBuilder<NavigationNode>();
        foreach (var node in root.SelectMany(n => n.DescendantsAndSelf()))
        {
            if (IsCollapsible(node))
            {
                _collapsibleNodeIds.Add(node, collapsibleNodes.Count);
                collapsibleNodes.Add(node);
            }
        }

        CollapsibleNodes = collapsibleNodes.ToImmutable();
    }

    /// <summary>The authored pages, sorted by folder and name, followed by the generated API reference.</summary>
    public ImmutableArray<NavigationNode> Root { get; }

    public string HomeTitle { get; }
    public string HomeUrl { get; }

    /// <summary>
    /// Nodes whose children can be shown in the sidebar of any page, indexed by their identifier. A page only renders the
    /// children of the open nodes; the others are loaded from the navigation index when they are expanded. Namespaces are
    /// not collapsible: a page only lists the types of its own namespace, so that large APIs do not inflate every page.
    /// </summary>
    public ImmutableArray<NavigationNode> CollapsibleNodes { get; }

    public static bool IsCollapsible(NavigationNode node) => !node.Children.IsEmpty && node.Kind != NavigationNode.NamespaceKind;

    public int GetCollapsibleNodeId(NavigationNode node) => _collapsibleNodeIds[node];

    /// <summary>Returns the navigation of a page. Previous and next links only follow the authored pages.</summary>
    public PageNavigation GetPageNavigation(string pagePath)
    {
        var trail = FindTrail(Root, pagePath) ?? [];
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

        return new PageNavigation(Root, [.. trail], breadcrumbs.ToImmutable(), previous, next);
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
