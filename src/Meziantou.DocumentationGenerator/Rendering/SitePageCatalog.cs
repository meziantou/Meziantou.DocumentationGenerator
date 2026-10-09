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

        foreach (var page in site.Content.Pages)
        {
            yield return PageRequest.Create<ArticlePage>(page.Route.FilePath, new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(ArticlePage.PagePath)] = page.Route.FilePath });
        }
    }
}
