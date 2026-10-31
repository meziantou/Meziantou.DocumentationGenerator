using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Rendering;
using Meziantou.DocumentationGenerator.Tests.EndToEnd;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class PublicationTests
{
    [Fact]
    public void Minification_RewritesUrlAttributesWithoutChangingTextOrExternalUrls()
    {
        var minifier = new HtmlMinifier("");
        var urls = new UrlBuilder(UrlBasePath.Parse("/docs/"));
        var html = minifier.Minify("""
            <!DOCTYPE html><html><head><link rel="canonical" href="https://example.com/docs/guide.html"></head><body>
            <a href="/docs/guide.html?x=1&amp;y=2#section">/docs/guide.html</a>
            <a href="#main">Skip</a><a href="//example.com/docs/">External</a>
            <img src="/docs/images/a%20b.png">
            <script src="/docs/_assets/site.js"></script>
            <div data-index="/docs/_assets/search-index.json" data-base-path="/docs/"></div>
            <pre>&lt;a href="/docs/guide.html"&gt;</pre>
            </body></html>
            """, url => urls.GetRelativeUrl("guides/install.html", url));
        var document = HtmlDocumentLoader.Parse(html);
        Assert.Equal("https://example.com/docs/guide.html", document.QuerySelector("link")!.GetAttribute("href"));
        Assert.Equal("../guide.html?x=1&y=2#section", document.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("/docs/guide.html", document.QuerySelector("a")!.TextContent);
        Assert.NotNull(document.QuerySelector("a[href='#main']"));
        Assert.NotNull(document.QuerySelector("a[href='//example.com/docs/']"));
        Assert.Equal("../images/a%20b.png", document.QuerySelector("img")!.GetAttribute("src"));
        Assert.Equal("../_assets/site.js", document.QuerySelector("script")!.GetAttribute("src"));
        Assert.Equal("../_assets/search-index.json", document.QuerySelector("div")!.GetAttribute("data-index"));
        Assert.Equal("../", document.QuerySelector("div")!.GetAttribute("data-base-path"));
        Assert.Equal("<a href=\"/docs/guide.html\">", document.QuerySelector("pre")!.TextContent);
    }

    [Fact]
    public void ParallelMinification_IsByteIdenticalToSequentialMinification()
    {
        var minifier = new HtmlMinifier("""
            p { display: block; }
            .inline { display: inline; }
            pre { white-space: pre; }
            .label::before { content: "Label "; }
            """);
        string[] pages =
        [
            "<!DOCTYPE html><html><head><title>Page</title></head><body><p> Some <strong>bold</strong> text. </p></body></html>",
            "<!DOCTYPE html><html><body><div class=\"inline\"> First </div> <span> second </span></body></html>",
            "<!DOCTYPE html><html><body><pre>  first\n    second\n</pre><p class=\"label\"> Value </p></body></html>",
            "<!DOCTYPE html><html><body><table><tr><th>Title</th></tr><tr><td><a href=\"/#example\">Value &amp; text</a></td></tr></table></body></html>",
        ];
        var expected = pages.Select(minifier.Minify).ToArray();
        var actual = new string[128];

        Parallel.For(0, actual.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => actual[i] = minifier.Minify(pages[i % pages.Length]));

        for (var i = 0; i < actual.Length; i++)
        {
            Assert.Equal(expected[i % pages.Length], actual[i]);
        }
    }

    [Fact]
    public async Task GeneratesACompleteStaticSite()
    {
        await using var temp = CreateSite();
        var result = await BuildAsync(temp);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        var output = temp.FullPath / "_site";
        Assert.True(File.Exists(output / "index.html"));
        Assert.True(File.Exists(output / "guide.html"));
        Assert.True(File.Exists(output / "_assets" / "site.css"));
        Assert.True(File.Exists(output / OutputManifest.FileName));

        var html = await File.ReadAllTextAsync(output / "guide.html", TestContext.Current.CancellationToken);
        Assert.StartsWith("<!DOCTYPE html>", html);
        var document = HtmlDocumentLoader.Parse(html);
        Assert.DoesNotContain(document.QuerySelectorAll<Meziantou.Framework.WebPlatform.Dom.Element>("script"), script => script.TextContent!.Contains("alert(1)", StringComparison.Ordinal));
        Assert.Equal("<script>alert(1)</script> | Test site", document.Title);
        Assert.Equal("<script>alert(1)</script>", document.QuerySelector("h1")!.TextContent);
        Assert.Equal("bold", document.QuerySelector("strong")!.TextContent);
        Assert.Equal("_assets/site.css", document.QuerySelector("link[rel=stylesheet]")!.GetAttribute("href"));
        Assert.DoesNotContain("blazor", html, ignoreCase: true);
        Assert.DoesNotContain("_framework", html);
        Assert.DoesNotContain("<!--!-->", html);
        Assert.Contains("Build succeeded", result.Output);
    }

    [Fact]
    public async Task RootAndSubpath_HaveTheSameLogicalContent()
    {
        await using var root = CreateSite();
        await using var docs = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(root)).ExitCode);
        Assert.Equal(ExitCodes.Success, (await BuildAsync(docs, "--base-path", "/docs/")).ExitCode);

        var rootFiles = GetFiles(root.FullPath / "_site");
        var docsFiles = GetFiles(docs.FullPath / "_site");
        Assert.Equal(rootFiles.Keys, docsFiles.Keys);
        Assert.NotNull(HtmlDocumentLoader.Parse(docsFiles["index.html"]).QuerySelector("a[href='guide.html']"));
        foreach (var file in rootFiles.Keys.Where(f => f.EndsWith(".html", StringComparison.Ordinal)))
        {
            Assert.Equal(rootFiles[file], docsFiles[file]);
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/build/path/")]
    public async Task GeneratedSite_CanBeRelocatedWithoutRebuilding(string basePath)
    {
        await using var site = await SiteBuilder.BuildAsync(
            [ApiModelTests.CreateKitchenSinkPackage()],
            files: new()
            {
                ["docs/index.md"] = "# Home\n\n[Install](Guides/install.md) or [browse the API](cref:T:Contoso.Library.Client).",
                ["docs/Guides/_folder.yml"] = "title: Guides\nslug: guides\n",
                ["docs/Guides/install.md"] = """
                    # Install

                    ## Setup

                    [This section](install.md?tab=cli#setup), [home](../index.md), [next](Deep/next.md),
                    or [the API member](cref:M:Contoso.Library.Client.Send(System.String)).

                    ![Diagram](images/a%20b.svg)
                    """,
                ["docs/Guides/Deep/next.md"] = "# Next\n\n[Previous](../install.md#setup)",
                ["docs/Guides/images/a b.svg"] = "<svg/>",
                ["logo.svg"] = "<svg/>",
                ["favicon.ico"] = "ico",
            },
            extraConfiguration: $$""" "site": { "title": "Portable site", "basePath": "{{basePath}}", "logo": "logo.svg", "favicon": "favicon.ico" }, """);
        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);

        foreach (var deploymentPath in new[] { "/", "/Meziantou.Framework/", "/another/nested/path/" })
        {
            Assert.Empty(SiteCrawler.Crawl(site.Output, deploymentPath, siteUrl: null));
        }

        foreach (var file in Directory.EnumerateFiles(site.Output, "*.html", SearchOption.AllDirectories))
        {
            var document = HtmlDocumentLoader.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            Assert.Empty(document.QuerySelectorAll<Meziantou.Framework.WebPlatform.Dom.Element>("[href^='/'], [src^='/'], [data-index^='/'], [data-base-path^='/']"));
        }

        var install = HtmlDocumentLoader.Parse(site.ReadFile("guides/install.html"));
        Assert.NotNull(install.QuerySelector("a[href='install.html?tab=cli#setup']"));
        Assert.Equal("images/a%20b.svg", install.QuerySelector("article img")!.GetAttribute("src"));
        Assert.Equal("../_assets/logo.svg", install.QuerySelector(".site-logo")!.GetAttribute("src"));
        var search = HtmlDocumentLoader.Parse(site.ReadFile("api/namespaces/Contoso.Library.html")).QuerySelector("[data-search]")!;
        Assert.Equal("../../_assets/search-index.json", search.GetAttribute("data-index"));
        Assert.Equal("../../", search.GetAttribute("data-base-path"));
    }

    [Fact]
    public async Task RepeatedBuilds_AreByteIdentical()
    {
        await using var first = CreateSite();
        await using var second = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(first)).ExitCode);
        Assert.Equal(ExitCodes.Success, (await BuildAsync(second)).ExitCode);
        Assert.Equal(ExitCodes.Success, (await BuildAsync(second)).ExitCode);

        Assert.Equal(GetFiles(first.FullPath / "_site"), GetFiles(second.FullPath / "_site"));
    }

    [Fact]
    public async Task FailedBuild_KeepsThePreviousSite()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);
        var before = GetFiles(temp.FullPath / "_site");

        temp.CreateTextFile("docs/broken.md", "[missing](missing.md)");
        var result = await BuildAsync(temp);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("DG5001", result.Error);
        Assert.Equal(before, GetFiles(temp.FullPath / "_site"));
        AssertNoStagingDirectory(temp);
    }

    [Fact]
    public async Task StaleFilesAreRemoved_UnrelatedFilesSurvive()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);
        temp.CreateTextFile("_site/CNAME", "docs.example.com");
        temp.CreateTextFile("_site/custom/keep.txt", "keep");

        File.Delete(temp.FullPath / "docs" / "guide.md");
        temp.CreateTextFile("docs/index.md", "# Home");
        var result = await BuildAsync(temp);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.False(File.Exists(temp.FullPath / "_site" / "guide.html"));
        Assert.True(File.Exists(temp.FullPath / "_site" / "CNAME"));
        Assert.True(File.Exists(temp.FullPath / "_site" / "custom" / "keep.txt"));
        AssertNoStagingDirectory(temp);
    }

    [Fact]
    public async Task UnrelatedFileCollision_FailsWithoutChanges()
    {
        await using var temp = CreateSite();
        temp.CreateTextFile("_site/guide.html", "user file");
        temp.CreateTextFile("_site/other.txt", "other");

        var result = await BuildAsync(temp);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("DG7004", result.Error);
        Assert.Contains("guide.html", result.Error);
        Assert.Equal("user file", await File.ReadAllTextAsync(temp.FullPath / "_site" / "guide.html", TestContext.Current.CancellationToken));
        Assert.False(File.Exists(temp.FullPath / "_site" / "index.html"));
    }

    [Fact]
    public async Task InterruptedPublication_IsCompletedByTheNextBuild()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);

        // A previous publication was interrupted after writing a file that is not in the manifest yet
        temp.CreateTextFile("_site/orphan.html", "generated by an interrupted build");
        temp.CreateTextFile("_site/" + OutputPublisher.JournalFileName, """{ "version": 1, "files": [ "orphan.html" ] }""");

        var result = await BuildAsync(temp);
        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.False(File.Exists(temp.FullPath / "_site" / "orphan.html"));
        Assert.False(File.Exists(temp.FullPath / "_site" / OutputPublisher.JournalFileName));
    }

    [Fact]
    public async Task Cancellation_KeepsThePreviousSite()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);
        var before = GetFiles(temp.FullPath / "_site");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var result = await CliRunner.RunWithCancellationAsync(["build", "--config", temp.FullPath / "docgen.json"], stages: null, cts.Token);

        Assert.Equal(ExitCodes.Cancelled, result.ExitCode);
        Assert.Equal(before, GetFiles(temp.FullPath / "_site"));
        AssertNoStagingDirectory(temp);
    }

    [Fact]
    public async Task Manifest_ListsFilesWithoutTimestampsOrMachinePaths()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);

        var manifest = OutputManifest.TryRead(temp.FullPath / "_site" / OutputManifest.FileName)!;
        Assert.Equal(["_assets/site.css", "_assets/site.js", "guide.html", "index.html"], manifest.Files.Where(f => !f.StartsWith("_assets/search", StringComparison.Ordinal) && f != "404.html" && f != "sitemap.xml"));
        var text = await File.ReadAllTextAsync(temp.FullPath / "_site" / OutputManifest.FileName, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(temp.FullPath.Value, text);
        Assert.DoesNotContain(DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-", text);
    }

    internal static TemporaryDirectory CreateSite()
    {
        var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docgen.json", """{ "version": 1, "site": { "title": "Test site" }, "content": { "root": "docs" } }""");
        temp.CreateTextFile("docs/index.md", "# Home\n\nRead the [guide](guide.md).");
        temp.CreateTextFile("docs/guide.md", "---\ntitle: <script>alert(1)</script>\nslug: guide\n---\n\nSome **bold** text.");
        return temp;
    }

    internal static Task<ProcessResult> BuildAsync(TemporaryDirectory directory, params string[] arguments)
    {
        return CliRunner.RunAsync(["build", "--config", directory.FullPath / "docgen.json", .. arguments]);
    }

    internal static SortedDictionary<string, string> GetFiles(FullPath directory)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(directory, file).Replace('\\', '/'), File.ReadAllText(file));
        }

        return result;
    }

    private static void AssertNoStagingDirectory(TemporaryDirectory directory)
    {
        Assert.Empty(Directory.EnumerateDirectories(directory.FullPath, ".*staging*"));
    }
}
