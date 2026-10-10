using System.Text;
using System.Xml;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>Generates the sitemap and robots.txt. No timestamp is written, so identical inputs produce identical files.</summary>
internal static class SiteMetadata
{
    public const string SitemapFileName = "sitemap.xml";
    public const string RobotsFileName = "robots.txt";
    public const string FaviconBaseName = "favicon";
    public const string LogoBaseName = "logo";
    public const string SocialImageBaseName = "social";

    /// <summary>Canonical URLs of the HTML pages, excluding the 404 page, sorted and without duplicates or fragments.</summary>
    public static string? BuildSitemap(SiteConfiguration configuration, UrlBuilder urls, IEnumerable<string> htmlFiles)
    {
        if (configuration.Site.SiteUrl is not { } siteUrl)
            return null;

        var locations = htmlFiles
            .Where(f => f != LinkService.NotFoundPath)
            .Select(f => UrlBuilder.GetAbsoluteUrl(siteUrl, urls.GetUrl(f)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), NewLineChars = "\n" };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var location in locations)
            {
                writer.WriteStartElement("url");
                writer.WriteElementString("loc", location);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>robots.txt is only meaningful at the origin root; it is not generated under a base path.</summary>
    public static string? BuildRobots(SiteConfiguration configuration, UrlBuilder urls)
    {
        if (!configuration.Site.BasePath.IsRoot)
            return null;

        return configuration.Site.Robots switch
        {
            RobotsPolicy.Allow => "User-agent: *\nAllow: /\n" + (configuration.Site.SiteUrl is { } siteUrl ? "Sitemap: " + UrlBuilder.GetAbsoluteUrl(siteUrl, urls.GetUrl(SitemapFileName)) + "\n" : ""),
            RobotsPolicy.Disallow => "User-agent: *\nDisallow: /\n",
            _ => null,
        };
    }
}
