using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Navigation;

/// <summary>
/// Builds the navigation from the folders of the content directory, sorted by name, followed by the generated API
/// reference: <c>API reference / {package} / {namespace} / {type}</c>.
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

    /// <summary>The last section of the navigation: one node per package with an API, in configuration order.</summary>
    private NavigationNode? CreateApiReferenceNode()
    {
        var packages = ImmutableArray.CreateBuilder<NavigationNode>();
        foreach (var package in catalog.Packages)
        {
            if (package.Namespaces.IsEmpty)
                continue;

            packages.Add(new NavigationNode
            {
                Title = package.Id,
                Url = links.GetPackageUrl(package.Id),
                PagePath = RouteNames.GetPackagePath(package.Id),
                Kind = NavigationNode.PackageKind,
                Children = CreateNamespaceNodes(package),
            });
        }

        if (packages.Count == 0)
            return null;

        return new NavigationNode { Title = ApiReferenceTitle, Url = packages[0].Url, IsGroup = true, Kind = NavigationNode.ApiReferenceKind, Children = packages.ToImmutable() };
    }

    /// <summary>Namespaces sorted ordinally, each listing its types (including nested types) sorted by metadata name.</summary>
    private ImmutableArray<NavigationNode> CreateNamespaceNodes(ApiPackageModel package)
    {
        return [.. package.Namespaces.Select(ns => new NavigationNode
        {
            Title = ns.IsGlobal ? "Global namespace" : ns.Name,
            Url = links.GetNamespaceUrl(package.Id, ns.Name),
            PagePath = RouteNames.GetNamespacePath(package.Id, ns.Name),
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
