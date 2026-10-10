using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Navigation;

/// <summary>Navigation of one page.</summary>
/// <param name="Sidebar">Tree displayed in the sidebar.</param>
/// <param name="ActiveTrail">Nodes from the root to the active node, used to expand the tree and mark the current page.</param>
/// <param name="Breadcrumbs">Breadcrumb links, from the home page to the parent of the page.</param>
/// <param name="Previous">Previous article in the table of contents.</param>
/// <param name="Next">Next article in the table of contents.</param>
internal sealed record PageNavigation(ImmutableArray<NavigationNode> Sidebar, ImmutableArray<NavigationNode> ActiveTrail, ImmutableArray<NavigationNode> Breadcrumbs, NavigationNode? Previous, NavigationNode? Next)
{
    public static PageNavigation Empty { get; } = new([], [], [], null, null);

    public bool IsActive(NavigationNode node) => ActiveTrail.Length > 0 && ReferenceEquals(ActiveTrail[^1], node);

    public bool IsInTrail(NavigationNode node) => ActiveTrail.Contains(node);
}
