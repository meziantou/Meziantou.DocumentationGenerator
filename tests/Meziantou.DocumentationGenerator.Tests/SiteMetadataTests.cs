using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class SiteMetadataTests
{
    [Fact]
    public async Task WithoutSiteUrl_NoCanonicalUrlAndNoSitemap()
    {
        await using var site = await BuildAsync("");

        var guide = HtmlDocumentLoader.Parse(site.ReadFile("guide.html"));
        Assert.Null(guide.QuerySelector("link[rel=canonical]"));
        Assert.Null(guide.QuerySelector("meta[property='og:url']"));
        Assert.Null(guide.QuerySelector("meta[name=robots]"));
        Assert.Equal("Guide | Test site", guide.Title);
        Assert.Equal("en", guide.DocumentElement!.GetAttribute("lang"));
        Assert.False(File.Exists(site.Output / "sitemap.xml"));
        Assert.False(File.Exists(site.Output / "robots.txt"));

        var notFound = HtmlDocumentLoader.Parse(site.ReadFile("404.html"));
        Assert.Equal("noindex", notFound.QuerySelector("meta[name=robots]")!.GetAttribute("content"));
        Assert.Equal("./", notFound.QuerySelector("main a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task RootHosting_CanonicalUrlsSitemapAndRobots()
    {
        await using var site = await BuildAsync(""" "site": { "title": "Test site", "url": "https://example.com", "robots": "allow" }, """);

        Assert.Equal("https://example.com/guide.html", HtmlDocumentLoader.Parse(site.ReadFile("guide.html")).QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("https://example.com/", HtmlDocumentLoader.Parse(site.ReadFile("index.html")).QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Null(HtmlDocumentLoader.Parse(site.ReadFile("404.html")).QuerySelector("link[rel=canonical]"));

        var sitemap = site.ReadFile("sitemap.xml");
        var locations = System.Xml.Linq.XDocument.Parse(sitemap).Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value).ToList();
        Assert.Contains("https://example.com/", locations);
        Assert.Contains("https://example.com/api/contoso.library/Contoso.Library.Client.html", locations);
        Assert.DoesNotContain(locations, l => l.Contains('#', StringComparison.Ordinal) || l.EndsWith("404.html", StringComparison.Ordinal));
        Assert.Distinct(locations);
        Assert.Equal(locations.Order(StringComparer.Ordinal), locations);
        Assert.DoesNotContain("lastmod", sitemap);

        // Every entry resolves to a generated page
        foreach (var location in locations)
        {
            var path = Uri.UnescapeDataString(new Uri(location).AbsolutePath.TrimStart('/'));
            Assert.True(File.Exists(site.Output / (path.Length == 0 || path.EndsWith('/', StringComparison.Ordinal) ? path + "index.html" : path)), location);
        }

        Assert.Equal("User-agent: *\nAllow: /\nSitemap: https://example.com/sitemap.xml\n", site.ReadFile("robots.txt"));
    }

    [Fact]
    public async Task SubpathHosting()
    {
        await using var site = await BuildAsync(""" "site": { "title": "Test site", "url": "https://example.com", "basePath": "/docs/", "robots": "allow" }, """);

        Assert.Equal("https://example.com/docs/guide.html", HtmlDocumentLoader.Parse(site.ReadFile("guide.html")).QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Contains("<loc>https://example.com/docs/</loc>", site.ReadFile("sitemap.xml"));
        Assert.False(File.Exists(site.Output / "robots.txt"));
        Assert.Contains("DG1010", site.Result.AllOutput);
    }

    [Fact]
    public async Task Disallow_AddsNoIndex()
    {
        await using var site = await BuildAsync(""" "site": { "title": "Test site", "robots": "disallow" }, """);

        Assert.Equal("User-agent: *\nDisallow: /\n", site.ReadFile("robots.txt"));
        Assert.Equal("noindex", HtmlDocumentLoader.Parse(site.ReadFile("guide.html")).QuerySelector("meta[name=robots]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task UnicodeTitlesAndBranding()
    {
        await using var site = await BuildAsync(
            """ "site": { "title": "Café 文档", "url": "https://example.com", "favicon": "favicon.ico", "logo": "logo.svg", "socialImage": "social.png", "language": "fr" }, """,
            extraFiles: new() { ["docs/unicode.md"] = "---\ntitle: Résumé — 日本語 & <tags> \"quoted\"\nslug: unicode\n---\nText.", ["favicon.ico"] = "ico", ["logo.svg"] = "<svg/>", ["social.png"] = "png" });

        var html = site.ReadFile("unicode.html");
        var document = HtmlDocumentLoader.Parse(html);
        Assert.Equal("Résumé — 日本語 & <tags> \"quoted\" | Café 文档", document.Title);
        Assert.Contains("<title>Résumé — 日本語", html);
        Assert.Null(document.QuerySelector("tags"));
        Assert.Equal("Résumé — 日本語 & <tags> \"quoted\"", document.QuerySelector("meta[property='og:title']")!.GetAttribute("content"));
        Assert.Equal("https://example.com/_assets/social.png", document.QuerySelector("meta[property='og:image']")!.GetAttribute("content"));
        Assert.Equal("summary_large_image", document.QuerySelector("meta[name='twitter:card']")!.GetAttribute("content"));
        Assert.Equal("_assets/favicon.ico", document.QuerySelector("link[rel=icon]")!.GetAttribute("href"));
        Assert.Equal("_assets/logo.svg", document.QuerySelector(".site-logo")!.GetAttribute("src"));
        Assert.Equal("fr", document.DocumentElement!.GetAttribute("lang"));
        Assert.True(File.Exists(site.Output / "_assets" / "social.png"));
    }

    [Fact]
    public async Task HeaderLinks_AreRenderedInHeader()
    {
        await using var site = await BuildAsync("""
            "site": {
              "headerLinks": [
                { "text": "Reference", "url": "https://example.com/reference", "icon": "📖" },
                { "text": "GitHub", "url": "https://github.com/example/project" }
              ]
            },
            """);

        var document = HtmlDocumentLoader.Parse(site.ReadFile("guide.html"));
        var links = document.QuerySelectorAll(".header-links .header-link");
        Assert.Equal(2, links.Length);
        var firstLink = document.QuerySelector(".header-links .header-link")!;
        var firstIcon = firstLink.QuerySelector(".header-link-icon")!;
        Assert.Equal("https://example.com/reference", firstLink.GetAttribute("href"));
        Assert.Equal("📖", firstIcon.TextContent);
        Assert.Equal("true", firstIcon.GetAttribute("aria-hidden"));
        Assert.Contains("Reference", firstLink.TextContent);

        var secondLink = document.QuerySelector(".header-links .header-link:nth-child(2)")!;
        Assert.Equal("https://github.com/example/project", secondLink.GetAttribute("href"));
        Assert.Null(secondLink.QuerySelector(".header-link-icon"));
        Assert.Equal("GitHub", secondLink.TextContent?.Trim());
    }

    [Fact]
    public async Task MetadataArtifacts_AreByteIdentical()
    {
        const string Configuration = """ "site": { "title": "Test site", "url": "https://example.com", "robots": "allow" }, """;
        await using var first = await BuildAsync(Configuration);
        await using var second = await BuildAsync(Configuration);

        Assert.Equal(first.ReadFile("sitemap.xml"), second.ReadFile("sitemap.xml"));
        Assert.Equal(first.ReadFile("robots.txt"), second.ReadFile("robots.txt"));
        Assert.Equal(first.ReadFile("404.html"), second.ReadFile("404.html"));
        Assert.Equal(first.ReadFile("_assets/search-index.json"), second.ReadFile("_assets/search-index.json"));
    }

    private static async Task<SiteBuilder> BuildAsync(string siteConfiguration, Dictionary<string, string>? extraFiles = null)
    {
        var files = new Dictionary<string, string>
        {
            ["docs/index.md"] = "# Home",
            ["docs/guide.md"] = "# Guide\n\nThe guide.",
        };

        foreach (var (key, value) in extraFiles ?? [])
        {
            files[key] = value;
        }

        var site = await SiteBuilder.BuildAsync([ApiModelTests.CreateKitchenSinkPackage()], files, extraConfiguration: siteConfiguration);
        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        return site;
    }
}
