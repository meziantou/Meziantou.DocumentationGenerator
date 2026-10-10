using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

internal static class HtmlDocumentLoader
{
    private static readonly HtmlParser Parser = new();

    public static IHtmlDocument Parse(string html) => Parser.ParseDocument(html);
}
