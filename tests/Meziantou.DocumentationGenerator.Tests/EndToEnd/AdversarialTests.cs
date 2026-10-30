using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Element = Meziantou.Framework.WebPlatform.Dom.Element;

namespace Meziantou.DocumentationGenerator.Tests.EndToEnd;

/// <summary>Hostile inputs through the complete pipeline. Feature-specific cases are covered by the unit tests of each stage.</summary>
public sealed class AdversarialTests
{
    [Fact]
    public async Task UnsafePackage_FailsWithoutOutput()
    {
        var package = new TestPackageBuilder("Evil", "1.0.0").AddFile("../../escape.txt", "evil").AddFile("lib/net8.0/Evil.dll", "x");
        await using var site = await SiteBuilder.BuildAsync([package]);

        Assert.Equal(ExitCodes.BuildFailed, site.Result.ExitCode);
        Assert.Contains("DG2005", site.Result.Error);
        Assert.False(Directory.Exists(site.Output));
        Assert.False(File.Exists(site.Directory.FullPath.Parent / "escape.txt"));
    }

    [Fact]
    public async Task HostileContent_NeverBecomesActiveMarkup()
    {
        var assembly = TestCompiler.Compile("Contoso.Hostile", "namespace Contoso.Hostile { public class Widget { } }");
        var package = new TestPackageBuilder("Contoso.Hostile", "1.0.0")
            .WithNuspecMetadata("<readme>README.md</readme>")
            .AddFile("README.md", "# Hostile\n\n<script>alert(1)</script><img src=x onerror=alert(2)><a href=\"javascript:alert(3)\">x</a>\n\n[link](javascript:alert(4))")
            .AddFile("lib/net8.0/Contoso.Hostile.dll", assembly.Assembly)
            .AddFile("lib/net8.0/Contoso.Hostile.xml", """
                <?xml version="1.0"?>
                <doc><members><member name="T:Contoso.Hostile.Widget">
                  <summary>&lt;script&gt;alert(5)&lt;/script&gt; <script>alert(6)</script> <see href="javascript:alert(7)">x</see> <a href="data:text/html,x" onclick="alert(8)">y</a></summary>
                </member></members></doc>
                """);
        await using var site = await SiteBuilder.BuildAsync([package], files: new()
        {
            ["docs/index.md"] = "---\ntitle: \"<script>alert(9)</script>\"\n---\n<iframe src=\"https://example.com\"></iframe><style>body{}</style>\n\n```html\n</code></pre><script>alert(10)</script>\n```",
            ["docs/01-Hostile/_folder.yml"] = "title: \"<b>bold</b><script>alert(11)</script>\"\ndescription: \"<img src=x onerror=alert(12)>\"",
            ["docs/01-Hostile/01-Page.md"] = "---\ntitle: \"<a href='javascript:alert(13)'>x</a>\"\n---\nText.",
        }, extraConfiguration: """ "diagnostics": { "severity": { "DG4008": "warning" } }, """);

        // Unsafe URLs are errors by default; they are lowered to check that the published site contains no active markup.
        // The package README is not published.
        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        Assert.Contains("warning DG4008", site.Result.AllOutput, StringComparison.Ordinal);
        foreach (var file in Directory.EnumerateFiles(site.Output, "*.html", SearchOption.AllDirectories))
        {
            var document = HtmlDocumentLoader.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
            var scripts = document.QuerySelectorAll<Element>("script").Where(s => !(s.GetAttribute("src")?.Contains("/_assets/", StringComparison.Ordinal) ?? false) && !s.TextContent!.StartsWith("document.documentElement.classList.add('js')", StringComparison.Ordinal)).ToList();
            Assert.Empty(scripts);
            Assert.Empty(document.QuerySelectorAll<Element>("iframe, style, object, embed"));
            Assert.DoesNotContain(document.All.SelectMany(e => e.Attributes), a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(document.QuerySelectorAll<Element>("[href], [src]").Select(e => e.GetAttribute("href") ?? e.GetAttribute("src")), u => u!.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || u.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Empty(SiteCrawler.Crawl(site.Output, "/", siteUrl: null));
    }

    [Fact]
    public async Task OversizedMarkdown_IsReportedAndSkipped()
    {
        await using var site = await SiteBuilder.BuildAsync([], files: new()
        {
            ["docs/index.md"] = "# Home",
            ["docs/huge.md"] = "# Huge\n\n" + new string('a', 5 * 1024 * 1024),
        });

        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        Assert.Contains("DG5002", site.Result.AllOutput);
        Assert.False(File.Exists(site.Output / "huge.html"));
    }
}
