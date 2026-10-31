using Meziantou.DocumentationGenerator.Components.Pages;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>Lists the pages of the site and the component rendering each of them.</summary>
internal static class SitePageCatalog
{
    public static IEnumerable<PageRequest> GetPages(SiteRenderContext site)
    {
        if (site.Content.HomePage is null)
        {
            yield return PageRequest.Create<HomePage>(LinkService.HomePath, new Dictionary<string, object?>(StringComparer.Ordinal));
        }

        yield return PageRequest.Create<NotFoundPage>(LinkService.NotFoundPath, new Dictionary<string, object?>(StringComparer.Ordinal));

        foreach (var page in site.Content.Pages)
        {
            yield return PageRequest.Create<ArticlePage>(page.Route.FilePath, new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(ArticlePage.PagePath)] = page.Route.FilePath });
        }

        if (site.Catalog.HasApi)
        {
            yield return PageRequest.Create<ApiReferencePage>(RouteNames.ApiReferencePath, new Dictionary<string, object?>(StringComparer.Ordinal));
        }

        foreach (var ns in site.Catalog.Namespaces)
        {
            yield return PageRequest.Create<NamespacePage>(RouteNames.GetNamespacePath(ns.Name), new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(NamespacePage.Namespace)] = ns.Name });
        }

        foreach (var package in site.Catalog.Packages)
        {
            if (package.Namespaces.IsEmpty)
                continue;

            yield return PageRequest.Create<PackagePage>(RouteNames.GetPackagePath(package.Id), new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(PackagePage.PackageId)] = package.Id });
            foreach (var type in package.GetAllTypes())
            {
                if (site.Links.GetTypeRoute(type) is { } route)
                {
                    yield return PageRequest.Create<TypePage>(route.FilePath, new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(TypePage.PagePath)] = route.FilePath });
                }
            }
        }
    }
}
