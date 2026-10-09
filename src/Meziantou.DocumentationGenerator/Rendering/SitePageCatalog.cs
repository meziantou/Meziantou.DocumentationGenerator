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

        foreach (var product in site.Configuration.Products)
        {
            yield return PageRequest.Create<ProductPage>(RouteNames.GetProductPath(product.Id), new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(ProductPage.ProductId)] = product.Id });
            var model = site.GetProductModel(product.Id);
            if (model is null || model.Namespaces.IsEmpty)
                continue;

            yield return PageRequest.Create<ApiReferencePage>(RouteNames.GetApiReferencePath(product.Id), new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(ApiReferencePage.ProductId)] = product.Id });
            foreach (var ns in model.Namespaces)
            {
                yield return PageRequest.Create<NamespacePage>(RouteNames.GetNamespacePath(product.Id, ns.Name), new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [nameof(NamespacePage.ProductId)] = product.Id,
                    [nameof(NamespacePage.Namespace)] = ns.Name,
                });
            }

            foreach (var type in model.GetAllTypes())
            {
                if (site.Links.GetTypeRoute(type) is { } route)
                {
                    yield return PageRequest.Create<TypePage>(route.FilePath, new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(TypePage.PagePath)] = route.FilePath });
                }
            }
        }
    }
}
