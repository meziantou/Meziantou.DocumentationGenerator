using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Tests.EndToEnd;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class FolderRedirectTests
{
    [Fact]
    public async Task Folder_RedirectsToItsFirstPage()
    {
        await using var site = await BuildAsync(""" "site": { "title": "Test site", "url": "https://example.com", "basePath": "/docs/" }, """);

        var redirect = HtmlDocumentLoader.Parse(site.ReadFile("assertions/index.html"));
        Assert.Equal("0; url=introduction.html", redirect.QuerySelector("meta[http-equiv=refresh]")!.GetAttribute("content"));
        Assert.Equal("https://example.com/docs/assertions/introduction.html", redirect.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("introduction.html", redirect.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Introduction | Test site", redirect.Title);
        Assert.Contains("location.replace(\"introduction.html\"+location.search+location.hash)", redirect.QuerySelector("script")!.TextContent, StringComparison.Ordinal);

        // A folder without direct page redirects to the first page of its first sub-folder
        Assert.Equal("0; url=nested/first.html", HtmlDocumentLoader.Parse(site.ReadFile("parent/index.html")).QuerySelector("meta[http-equiv=refresh]")!.GetAttribute("content"));
        Assert.Equal("0; url=first.html", HtmlDocumentLoader.Parse(site.ReadFile("parent/nested/index.html")).QuerySelector("meta[http-equiv=refresh]")!.GetAttribute("content"));

        Assert.Empty(SiteCrawler.Crawl(site.Output, "/docs/", "https://example.com"));

        // Redirects are not listed in the sitemap
        Assert.DoesNotContain("/assertions/<", site.ReadFile("sitemap.xml"), StringComparison.Ordinal);
        Assert.Contains("https://example.com/docs/assertions/introduction.html", site.ReadFile("sitemap.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingIndexPage_IsKept()
    {
        await using var site = await BuildAsync(""" "site": { "title": "Test site" }, """, new()
        {
            ["docs/assertions/index.md"] = "---\nslug: index\n---\n# Assertions home",
            ["docs/api/_folder.yml"] = "title: API notes\nslug: api",
            ["docs/api/notes.md"] = "# Notes",
        });

        Assert.Contains("Assertions home", site.ReadFile("assertions/index.html"), StringComparison.Ordinal);
        Assert.Null(HtmlDocumentLoader.Parse(site.ReadFile("api/index.html")).QuerySelector("meta[http-equiv=refresh]"));
        Assert.Contains(" 0 warning(s)", site.Result.AllOutput, StringComparison.Ordinal);
    }

    private static async Task<SiteBuilder> BuildAsync(string siteConfiguration, Dictionary<string, string>? extraFiles = null)
    {
        var files = new Dictionary<string, string>
        {
            ["docs/index.md"] = "# Home",
            ["docs/assertions/_folder.yml"] = "title: Assertions",
            ["docs/assertions/01-introduction.md"] = "# Introduction",
            ["docs/assertions/02-usage.md"] = "# Usage",
            ["docs/parent/_folder.yml"] = "title: Parent",
            ["docs/parent/nested/_folder.yml"] = "title: Nested",
            ["docs/parent/nested/first.md"] = "# First",
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
