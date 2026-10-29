using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class MarkdownTests
{
    [Fact]
    public async Task AuthoredPages_RoutesTitlesAndRelativeLinks()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = """
                ---
                title: Welcome
                description: The home page.
                ---

                See [the guide](guides/getting-started.md#install), [the folder](guides/), and [a query](guides/getting-started.md?tab=cli#install).
                """,
            ["docs/guides/index.md"] = "# Guides\n\nAll guides.",
            ["docs/guides/getting-started.md"] = """
                # Getting started

                First paragraph used as the description.

                ## Install

                ## Install

                Back to [home](../index.md) and [missing](missing.md).
                """,
            ["docs/guides/custom.md"] = "---\nslug: custom-url\n---\n# Custom",
        });

        var pages = build.Context.Content.Pages;
        Assert.Equal(["guides/custom", "guides/getting-started", "guides/index", "index"], pages.Select(p => p.Id));
        Assert.Equal(["/docs/guides/custom-url.html", "/docs/guides/getting-started.html", "/docs/guides/guides.html", "/docs/"], pages.Select(p => p.Url));

        var index = pages.Single(p => p.Id == "index");
        Assert.Equal("Welcome", index.Title);
        Assert.Equal("The home page.", index.Rendered!.Description);
        Assert.Contains("""<a href="/docs/guides/getting-started.html#install">the guide</a>""", index.Rendered.Html);
        Assert.Contains("""<a href="/docs/guides/custom-url.html">the folder</a>""", index.Rendered.Html);
        Assert.Contains("""<a href="/docs/guides/getting-started.html?tab=cli#install">a query</a>""", index.Rendered.Html);

        var guide = pages.Single(p => p.Id == "guides/getting-started");
        Assert.Equal("Getting started", guide.Title);
        Assert.Equal("First paragraph used as the description.", guide.Rendered!.Description);
        Assert.DoesNotContain("<h1", guide.Rendered.Html);
        Assert.Equal([new HeadingInfo(2, "install", "Install"), new HeadingInfo(2, "install-1", "Install")], guide.Rendered.Headings);
        Assert.Contains("""<a href="/docs/">home</a>""", guide.Rendered.Html);
        Assert.Contains("and missing.", guide.Rendered.Html);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5001" && d.Severity == DiagnosticSeverity.Error && d.Location?.FilePath == "docs/guides/getting-started.md" && d.Location.Line == 9);
    }

    [Fact]
    public async Task Extensions_TablesAlertsTaskListsAndCode()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = """
                | Name | Value |
                | ---- | ----: |
                | a    | 1     |

                > [!WARNING]
                > Be careful.

                - [x] done
                - [ ] todo

                ```csharp
                public class Sample { }
                ```

                ```unknownlanguage
                <b>not html</b>
                ```

                Special characters: <, &, "quotes" and émojis 🎉.
                """,
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.Contains("<table>", html);
        Assert.Contains("markdown-alert-warning", html);
        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("""<pre><code class="hljs language-csharp"><span class="hljs-keyword">public</span>""", html);
        Assert.Contains("&lt;b&gt;not html&lt;/b&gt;", html);
        Assert.Contains("&lt;, &amp;", html);
        Assert.Contains("🎉", html);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5010");
    }

    [Fact]
    public async Task CrefLinks()
    {
        await using var build = await BuildAsync(
            packages: [ApiModelTests.CreateKitchenSinkPackage()],
            files: new()
            {
                ["docs/index.md"] = """
                    Use [](cref:T:Contoso.Library.Client), [the send method](cref:M:Contoso.Library.Client.Send(System.String)), [](cref:Contoso.Library.Collections.Tree{T}) and [](cref:T:Missing.Type).
                    """,
            });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.Contains("""<a href="/docs/contoso.library/api/Contoso.Library.Client.html"><code>Client</code></a>""", html);
        Assert.Contains("""<a href="/docs/contoso.library/api/Contoso.Library.Client.html#Send(System.String)">the send method</a>""", html);
        Assert.Contains("""<a href="/docs/contoso.library/api/Contoso.Library.Collections.Tree-1.html"><code>Tree&lt;T&gt;</code></a>""", html);
        Assert.Contains("<code>Type</code>", html);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4001" && d.Message.Contains("Missing.Type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsafeContent_IsRemoved()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = """
                <script>alert('xss')</script>
                <img src="x.png" onerror="alert(1)">
                <iframe src="https://example.com"></iframe>
                <div style="color:red" onclick="alert(1)">styled</div>

                [click](javascript:alert(1)) and [data](data:text/html,hi) and <a href="vbscript:x">raw</a>

                [secret](../../secret.txt) ![image](../outside.png)
                """,
            ["secret.txt"] = "secret",
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.DoesNotContain("<script", html, ignoreCase: true);
        Assert.DoesNotContain("onerror", html, ignoreCase: true);
        Assert.DoesNotContain("onclick", html, ignoreCase: true);
        Assert.DoesNotContain("<iframe", html, ignoreCase: true);
        Assert.DoesNotContain("style=", html, ignoreCase: true);
        Assert.DoesNotContain("javascript:", html, ignoreCase: true);
        Assert.DoesNotContain("vbscript:", html, ignoreCase: true);
        Assert.DoesNotContain("data:text", html, ignoreCase: true);
        Assert.DoesNotContain("secret.txt", html);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4008");
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5009" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task HostileCodeBlock_DoesNotStall()
    {
        var hostile = "```javascript\n" + string.Concat(Enumerable.Repeat("/*a*/ `${`${'", 2000)) + "\n```";
        var start = System.Diagnostics.Stopwatch.StartNew();
        await using var build = await BuildAsync(files: new() { ["docs/index.md"] = hostile });

        Assert.InRange(start.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(60));
        Assert.Contains("<pre><code", build.Context.Content.Pages.Single().Rendered!.Html);
    }

    [Fact]
    public async Task LocalAssets_AreTrackedAndCopied()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = """
                ![logo](images/logo.png "Logo") [download](files/sample%20file.zip) ![missing](images/missing.png)
                <img src="images/logo.png" alt="raw">
                """,
            ["docs/images/logo.png"] = "png",
            ["docs/files/sample file.zip"] = "zip",
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.Contains("""<img src="/docs/images/logo.png" alt="logo" title="Logo">""", html);
        Assert.Contains("""<img src="/docs/images/logo.png" alt="raw">""", html);
        Assert.Contains("download</a> missing\n", html);
        Assert.Contains("""<a href="/docs/files/sample%20file.zip">download</a>""", html);
        Assert.Equal(["files/sample file.zip", "images/logo.png"], build.Context.Assets!.Assets.Select(a => a.OutputPath));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5008" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task LinkedFiles_ArePublishedUnderTheFolderUrl()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/01-Imaging/_folder.yml"] = "title: Imaging",
            ["docs/01-Imaging/07-Drawing/_folder.yml"] = "title: Drawing",
            ["docs/01-Imaging/07-Drawing/01-Shapes.md"] = "# Shapes\n\n![badge](images/DrawBadge.png) ![shared](../../shared/01-common/logo.png) [sample](samples/data.zip)",
            ["docs/01-Imaging/07-Drawing/images/DrawBadge.png"] = "png",
            ["docs/01-Imaging/07-Drawing/samples/data.zip"] = "zip",
            ["docs/shared/01-common/logo.png"] = "png",
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.Contains("""<img src="/docs/imaging/drawing/images/DrawBadge.png" alt="badge">""", html);
        Assert.Contains("""<img src="/docs/shared/01-common/logo.png" alt="shared">""", html);
        Assert.Contains("""<a href="/docs/imaging/drawing/samples/data.zip">sample</a>""", html);
        Assert.Equal(["imaging/drawing/images/DrawBadge.png", "imaging/drawing/samples/data.zip", "shared/01-common/logo.png"], build.Context.Assets!.Assets.Select(a => a.OutputPath));
    }

    [Fact]
    public async Task LinkedFiles_AtTheSamePath_AreAnError()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/01-Guides/_folder.yml"] = "title: Guides",
            ["docs/01-Guides/01-Page.md"] = "![a](images/logo.png)",
            ["docs/01-Guides/images/logo.png"] = "png",
            ["docs/guides/images/logo.png"] = "other png",
            ["docs/index.md"] = "![b](guides/images/logo.png)",
        });

        Assert.Contains(build.Diagnostics, d => d.Code == "DG4005" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task BrokenLinks_AreErrors()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = """
                [excluded](AGENTS.md) [empty folder](empty/) [missing folder](nope/) [root](./) [image folder](01-Guides/images/)

                ![missing](01-Guides/images/missing.png)
                """,
            ["docs/AGENTS.md"] = "Instructions.",
            ["docs/empty/notes.txt"] = "not a page",
            ["docs/01-Guides/images/logo.png"] = "png",
            ["docs/01-Guides/01-First.md"] = "# First",
        }, contentJson: """ "content": { "root": "docs", "exclude": [ "**/AGENTS.md" ] }, """);

        var html = build.Context.Content.HomePage!.Rendered!.Html;
        Assert.Contains("""<a href="/docs/">root</a>""", html);
        Assert.DoesNotContain("AGENTS", html, StringComparison.Ordinal);
        var errors = build.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Contains(errors, d => d.Code == "DG5001" && d.Message.Contains("AGENTS.md' is excluded", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Code == "DG5001" && d.Message.Contains("empty/", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Code == "DG5001" && d.Message.Contains("01-Guides/images/", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Code == "DG5008" && d.Message.Contains("nope/", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Code == "DG5008" && d.Message.Contains("missing.png", StringComparison.Ordinal) && d.Location?.Line == 3);
    }

    [Fact]
    public async Task PackageReadme_IsNotPublished()
    {
        var package = new TestPackageBuilder("Contoso.Readme", "1.0.0")
            .WithNuspecMetadata("<readme>docs/README.md</readme>")
            .AddFile("docs/README.md", "# Contoso.Readme\n\n![logo](images/logo.png)")
            .AddFile("docs/images/logo.png", "png");
        await using var build = await BuildAsync(packages: [package], files: new() { ["docs/01-Page.md"] = "# Page" });

        Assert.Equal(["01-Page"], build.Context.Content.Pages.Select(p => p.Id));
        Assert.Empty(build.Context.Assets!.Assets);
        Assert.DoesNotContain(build.Context.Links!.Routes, r => r.FilePath.StartsWith("contoso.readme/", StringComparison.Ordinal) && !r.FilePath.StartsWith("contoso.readme/api/", StringComparison.Ordinal));
    }

    private static async Task<TestBuild> BuildAsync(
        IReadOnlyList<TestPackageBuilder>? packages = null,
        Dictionary<string, string>? files = null,
        string? contentJson = null)
    {
        packages ??= [];
        files ??= [];
        var packageEntries = packages.Select(p => TestBuild.PackageReference(p)).ToList();
        var content = contentJson ?? (files.Keys.Any(k => k.StartsWith("docs/", StringComparison.Ordinal)) ? """ "content": { "root": "docs" }, """ : "");
        var build = TestBuild.Create($$"""
            {
              "version": 1,
              "site": { "basePath": "/docs/" },
              {{content}}
              "packages": [ {{string.Join(", ", packageEntries)}} ]
            }
            """, packages, directory =>
        {
            foreach (var (path, text) in files)
            {
                directory.CreateTextFile(path, text);
            }
        });
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new ContentStage());
        return build;
    }
}
