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

    public string SiteTitle => Configuration.Site.Title;

    /// <summary>Formats a page title: <c>{page} | {site}</c>, or the site title alone for the home page.</summary>
    public string GetDocumentTitle(string? pageTitle) => string.IsNullOrEmpty(pageTitle) || string.Equals(pageTitle, SiteTitle, StringComparison.Ordinal) ? SiteTitle : pageTitle + " | " + SiteTitle;
}
