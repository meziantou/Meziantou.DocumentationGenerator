using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>Rewrites the URLs of the rendered pages and optionally minifies them before they are written to the output.</summary>
internal sealed class HtmlMinifier(string styleSheet)
{
    private static readonly string[] UrlAttributes = ["href", "src", "data-index", "data-base-path"];

    private static readonly HtmlMinifyFormatter Formatter = new()
    {
        StyleElementFormatter = new CssMinifyFormatter(),
        StyleAttributeFormatter = new CssMinifyFormatter(),
    };

    public string Minify(string html) => Minify(html, rewriteUrl: null);

    /// <summary>Rewrites the URLs without minifying the markup. This is much faster than minification, which computes the styles of every element.</summary>
    public static string RewriteUrls(string html, Func<string, string> rewriteUrl)
    {
        var document = HtmlDocumentParser.ParseDocument(html);
        RewriteUrls(document, rewriteUrl);
        return document.ToHtml();
    }

    public string Minify(string html, Func<string, string>? rewriteUrl)
    {
        var document = HtmlDocumentParser.ParseDocument(html);
        if (rewriteUrl is not null)
        {
            RewriteUrls(document, rewriteUrl);
        }

        // The minifier keeps or removes whitespace based on the computed styles. The site style sheet is referenced by a
        // link element, which the parser does not load, so it is adopted to compute the same styles as the browser
        var sheet = new CssStyleSheet(document, new CssStyleSheetInit());
        sheet.ReplaceSync(styleSheet);
        document.AdoptedStyleSheets.Add(sheet);

        return document.ToHtml(Formatter);
    }

    private static void RewriteUrls(Document document, Func<string, string> rewriteUrl)
    {
        foreach (var element in document.QuerySelectorAll<Element>("[href], [src], [data-index], [data-base-path]"))
        {
            foreach (var attribute in UrlAttributes)
            {
                if (element.GetAttribute(attribute) is { } url)
                {
                    element.SetAttribute(attribute, rewriteUrl(url));
                }
            }
        }
    }
}
