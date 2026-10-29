using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Navigation;

/// <summary>
/// Builds the navigation from the folders of the content directory, sorted by name, followed by the generated API
/// reference: <c>API reference / {product} / {namespace} / {type}</c>.
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

                case ContentFolder child when child.FirstPage is not null:
                    yield return new NavigationNode { Title = child.Title, Children = [.. CreateEntryNodes(child)] };
                    break;
            }
        }
    }

    private static NavigationNode CreatePageNode(AuthoredPage page) => new() { Title = page.Title, Url = page.Url, PagePath = page.Route.FilePath };

    /// <summary>The last section of the navigation: one node per product with an API, in configuration order.</summary>
    private NavigationNode? CreateApiReferenceNode()
    {
        var products = ImmutableArray.CreateBuilder<NavigationNode>();
        foreach (var product in configuration.Products)
        {
            var namespaces = CreateApiNodes(product.Id);
            if (namespaces.IsEmpty)
                continue;

            products.Add(new NavigationNode
            {
                Title = product.Name,
                Url = links.GetApiReferenceUrl(product.Id),
                PagePath = RouteNames.GetApiReferencePath(product.Id),
                Kind = NavigationNode.ProductKind,
                Children = namespaces,
            });
        }

        if (products.Count == 0)
            return null;

        return new NavigationNode { Title = ApiReferenceTitle, Kind = NavigationNode.ApiReferenceKind, Children = products.ToImmutable() };
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
            Kind = NavigationNode.NamespaceKind,
            Children = [.. ns.Types.Select(type => new NavigationNode
            {
                Title = type.DisplayName,
                Url = links.GetUrl(type),
                PagePath = links.GetTypeRoute(type)?.FilePath,
                Kind = NavigationNode.TypeKind,
            })],
        })];
    }
}
