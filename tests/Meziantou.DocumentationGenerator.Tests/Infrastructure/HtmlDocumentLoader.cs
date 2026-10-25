using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

internal static class HtmlDocumentLoader
{
    public static Document Parse(string html) => HtmlDocumentParser.ParseDocument(html);
}
