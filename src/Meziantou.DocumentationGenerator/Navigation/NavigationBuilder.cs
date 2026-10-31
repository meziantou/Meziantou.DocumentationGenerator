using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Navigation;

/// <summary>
/// Builds the navigation from the folders of the content directory, sorted by name, followed by the generated API
/// reference: <c>API reference / {namespace} / {type}</c>.
/// </summary>
internal sealed class NavigationBuilder(SiteConfiguration configuration, ContentModel content, ApiCatalog catalog, LinkService links)
{
    public const string ApiReferenceTitle = "API reference";

    public SiteNavigation Build()
    {
        var root = ImmutableArray.CreateBuilder<NavigationNode>();
        if (content.HomePage is { } home)
        {
            root.Add(CreatePageNode(home));
        }

        if (content.Root is { } folder)
        {
            root.AddRange(CreateEntryNodes(folder));
        }

        var api = CreateApiReferenceNode();
        if (api is not null)
        {
            root.Add(api);
        }

        var homeTitle = content.HomePage?.Title ?? configuration.Site.Title;
        return new SiteNavigation(root.ToImmutable(), homeTitle, links.HomeUrl);
    }

    private IEnumerable<NavigationNode> CreateEntryNodes(ContentFolder folder)
    {
        foreach (var entry in folder.Entries)
        {
            switch (entry)
            {
                case AuthoredPage page when page != content.HomePage:
                    yield return CreatePageNode(page);
                    break;

                case ContentFolder { FirstPage: { } firstPage } child:
                    yield return new NavigationNode { Title = child.Title, Url = firstPage.Url, IsGroup = true, Children = [.. CreateEntryNodes(child)] };
                    break;
            }
        }
    }

    private static NavigationNode CreatePageNode(AuthoredPage page) => new() { Title = page.Title, Url = page.Url, PagePath = page.Route.FilePath };

    /// <summary>
    /// The last section of the navigation: the API reference page, listing the namespaces of every package sorted
    /// ordinally, each listing its types (including nested types) sorted by metadata name.
    /// </summary>
    private NavigationNode? CreateApiReferenceNode()
    {
        if (!catalog.HasApi)
            return null;

        return new NavigationNode
        {
            Title = ApiReferenceTitle,
            Url = links.ApiReferenceUrl,
            PagePath = RouteNames.ApiReferencePath,
            Kind = NavigationNode.ApiReferenceKind,
            Children = [.. catalog.Namespaces.Select(ns => new NavigationNode
            {
                Title = ns.IsGlobal ? "Global namespace" : ns.Name,
                Url = links.GetNamespaceUrl(ns.Name),
                PagePath = RouteNames.GetNamespacePath(ns.Name),
                Kind = NavigationNode.NamespaceKind,
                Children = [.. ns.Types.Select(type => new NavigationNode
                {
                    Title = type.DisplayName,
                    Url = links.GetUrl(type),
                    PagePath = links.GetTypeRoute(type)?.FilePath,
                    Kind = NavigationNode.TypeKind,
                })],
            })],
        };
    }
}
