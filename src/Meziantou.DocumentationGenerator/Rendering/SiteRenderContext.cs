using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Navigation;
using Meziantou.DocumentationGenerator.SourceLinks;
using Meziantou.DocumentationGenerator.XmlDocs;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>Services available to Razor components while rendering the site.</summary>
internal sealed class SiteRenderContext
{
    public required SiteConfiguration Configuration { get; init; }
    public required LinkService Links { get; init; }
    public required SiteNavigation Navigation { get; init; }
    public required ContentModel Content { get; init; }
    public required ApiCatalog Catalog { get; init; }
    public required DocumentationProvider Documentation { get; init; }
    public required SourceLinkProvider SourceLinks { get; init; }
    public required HighlightingService Highlighting { get; init; }
    public required DiagnosticBag Diagnostics { get; init; }
    public required System.Collections.Immutable.ImmutableArray<Discovery.DiscoveredPackage> Packages { get; init; }

    private Dictionary<string, ApiTypeModel>? _typesByPath;

    public string SiteTitle => Configuration.Site.Title;

    public ApiTypeModel GetTypeByPagePath(string pagePath)
    {
        _typesByPath ??= Catalog.Products.SelectMany(p => p.GetAllTypes()).Where(t => Links.GetTypeRoute(t) is not null).ToDictionary(t => Links.GetTypeRoute(t)!.FilePath, StringComparer.Ordinal);
        return _typesByPath[pagePath];
    }

    public ApiProductModel? GetProductModel(string productId) => Catalog.Products.FirstOrDefault(p => p.Product.Id == productId);

    public ProductConfiguration GetProduct(string productId) => Configuration.Products.Single(p => p.Id == productId);

    /// <summary>Discovered packages of a product, in configuration order.</summary>
    public IReadOnlyList<Discovery.DiscoveredPackage> GetPackages(ProductConfiguration product)
    {
        return [.. product.Packages.Select(id => Packages.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))).Where(p => p is not null).Select(p => p!)];
    }

    public string? GetProductDescription(ProductConfiguration product) => product.Description ?? GetPackages(product).Select(p => p.Package.Metadata.Description).FirstOrDefault(d => d is not null);

    /// <summary>Formats a page title: <c>{page} | {site}</c>, or the site title alone for the home page.</summary>
    public string GetDocumentTitle(string? pageTitle) => string.IsNullOrEmpty(pageTitle) || string.Equals(pageTitle, SiteTitle, StringComparison.Ordinal) ? SiteTitle : pageTitle + " | " + SiteTitle;
}
