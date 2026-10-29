using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>Minifies the rendered pages before they are written to the output.</summary>
internal sealed class HtmlMinifier(string styleSheet)
{
    private static readonly HtmlMinifyFormatter Formatter = new()
    {
        StyleElementFormatter = new CssMinifyFormatter(),
        StyleAttributeFormatter = new CssMinifyFormatter(),
    };

    public string Minify(string html)
    {
        var document = HtmlDocumentParser.ParseDocument(html);

        // The minifier keeps or removes whitespace based on the computed styles. The site style sheet is referenced by a
        // link element, which the parser does not load, so it is adopted to compute the same styles as the browser
        var sheet = new CssStyleSheet(document, new CssStyleSheetInit());
        sheet.ReplaceSync(styleSheet);
        document.AdoptedStyleSheets.Add(sheet);

        return document.ToHtml(Formatter);
    }
}
