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
            ["docs/guides/custom.md"] = "---\nslug: custom/url\n---\n# Custom",
        });

        var pages = build.Context.Content.Pages;
        Assert.Equal(["guides/custom", "guides/getting-started", "guides/index", "index"], pages.Select(p => p.Id));
        Assert.Equal(["/docs/custom/url.html", "/docs/guides/getting-started.html", "/docs/guides/", "/docs/"], pages.Select(p => p.Url));

        var index = pages.Single(p => p.Id == "index");
        Assert.Equal("Welcome", index.Title);
        Assert.Equal("The home page.", index.Rendered!.Description);
        Assert.Contains("""<a href="/docs/guides/getting-started.html#install">the guide</a>""", index.Rendered.Html);
        Assert.Contains("""<a href="/docs/guides/">the folder</a>""", index.Rendered.Html);
        Assert.Contains("""<a href="/docs/guides/getting-started.html?tab=cli#install">a query</a>""", index.Rendered.Html);

        var guide = pages.Single(p => p.Id == "guides/getting-started");
        Assert.Equal("Getting started", guide.Title);
        Assert.Equal("First paragraph used as the description.", guide.Rendered!.Description);
        Assert.DoesNotContain("<h1", guide.Rendered.Html);
        Assert.Equal([new HeadingInfo(2, "install", "Install"), new HeadingInfo(2, "install-1", "Install")], guide.Rendered.Headings);
        Assert.Contains("""<a href="/docs/">home</a>""", guide.Rendered.Html);
        Assert.Contains("and missing.", guide.Rendered.Html);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5001" && d.Location?.FilePath == "docs/guides/getting-started.md" && d.Location.Line == 9);
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
        Assert.Contains("""<img src="/docs/images/logo.png" alt="logo" title="Logo" />""", html);
        Assert.Contains("""<img src="/docs/images/logo.png" alt="raw" />""", html);
        Assert.Contains("download</a> missing\n", html);
        Assert.Contains("""<a href="/docs/files/sample%20file.zip">download</a>""", html);
        Assert.Equal(["files/sample file.zip", "images/logo.png"], build.Context.Assets!.Assets.Select(a => a.OutputPath));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5008");
    }

    [Fact]
    public async Task PackageReadme_AssetsAndRepositoryLinks()
    {
        var package = new TestPackageBuilder("Contoso.Readme", "1.0.0")
            .WithNuspecMetadata("""<readme>docs/README.md</readme><repository type="git" url="https://github.com/contoso/readme.git" commit="0123456789abcdef0123456789abcdef01234567" />""")
            .AddFile("docs/README.md", """
                # Contoso.Readme

                [![Build](https://github.com/contoso/readme/actions/workflows/ci.yml/badge.svg)](https://github.com/contoso/readme/actions)

                ![logo](images/logo.png) ![diagram](diagram.svg) [contributing](CONTRIBUTING.md) [api](cref:T:Contoso.Missing)
                """)
            .AddFile("docs/images/logo.png", "png");
        await using var build = await BuildAsync(packages: [package]);

        var landing = build.Context.Content.ProductLandingPages["contoso.readme"];
        Assert.Equal("Contoso.Readme", landing.Title);
        Assert.Contains("""<img src="https://github.com/contoso/readme/actions/workflows/ci.yml/badge.svg" alt="Build" />""", landing.Html);
        Assert.Contains("""<img src="/docs/contoso.readme/_readme/docs/images/logo.png" alt="logo" />""", landing.Html);
        Assert.Contains("""<img src="https://raw.githubusercontent.com/contoso/readme/0123456789abcdef0123456789abcdef01234567/diagram.svg" alt="diagram" />""", landing.Html);
        Assert.Contains("""<a href="https://github.com/contoso/readme/blob/0123456789abcdef0123456789abcdef01234567/CONTRIBUTING.md">contributing</a>""", landing.Html);
        var asset = Assert.Single(build.Context.Assets!.Assets);
        Assert.Equal("docs/images/logo.png", asset.PackageEntry);
    }

    [Fact]
    public async Task PackageReadme_WithoutRepositoryMetadata_DoesNotGuessLinks()
    {
        var package = new TestPackageBuilder("Contoso.Readme", "1.0.0")
            .WithNuspecMetadata("<readme>README.md</readme>")
            .AddFile("README.md", "![diagram](diagram.svg) [contributing](CONTRIBUTING.md)");
        await using var build = await BuildAsync(packages: [package]);

        var landing = build.Context.Content.ProductLandingPages["contoso.readme"];
        Assert.DoesNotContain("<img", landing.Html);
        Assert.DoesNotContain("<a ", landing.Html);
        Assert.Equal(2, build.Diagnostics.Count(d => d.Code == "DG3004"));
    }

    [Fact]
    public async Task LandingPageSelection()
    {
        var first = new TestPackageBuilder("Contoso.A", "1.0.0").WithNuspecMetadata("<readme>README.md</readme>").AddFile("README.md", "# A readme");
        var second = new TestPackageBuilder("Contoso.B", "1.0.0").WithNuspecMetadata("<readme>README.md</readme>").AddFile("README.md", "# B readme");
        await using var build = await BuildAsync(
            packages: [first, second],
            files: new() { ["local/landing.md"] = "# Local landing", ["local/override.md"] = "# Override" },
            productsJson: """
                "products": [
                  { "id": "readme", "packages": [ "Contoso.A", "Contoso.B" ], "readmePackage": "Contoso.B" },
                  { "id": "landing", "packages": [ "Contoso.C" ], "landingPage": "local/landing.md" },
                  { "id": "override", "packages": [ "Contoso.D" ] }
                ],
                """,
            setup: d =>
            {
                new TestPackageBuilder("Contoso.C", "1.0.0").Save(d.FullPath / "packages", "contoso.c.1.0.0.nupkg");
                new TestPackageBuilder("Contoso.D", "1.0.0").Save(d.FullPath / "packages", "contoso.d.1.0.0.nupkg");
            },
            packageOverrides: new()
            {
                ["Contoso.C"] = """{ "id": "Contoso.C", "version": "1.0.0", "path": "packages/contoso.c.1.0.0.nupkg" }""",
                ["Contoso.D"] = """{ "id": "Contoso.D", "version": "1.0.0", "path": "packages/contoso.d.1.0.0.nupkg", "readme": "local/override.md" }""",
            });

        Assert.Equal("B readme", build.Context.Content.ProductLandingPages["readme"].Title);
        Assert.Equal("Local landing", build.Context.Content.ProductLandingPages["landing"].Title);
        Assert.Equal("Override", build.Context.Content.ProductLandingPages["override"].Title);
    }

    private static async Task<TestBuild> BuildAsync(
        IReadOnlyList<TestPackageBuilder>? packages = null,
        Dictionary<string, string>? files = null,
        string productsJson = "",
        Action<Meziantou.Framework.TemporaryDirectory>? setup = null,
        Dictionary<string, string>? packageOverrides = null)
    {
        packages ??= [];
        files ??= [];
        var packageEntries = packages.Select(p => TestBuild.PackageReference(p)).Concat(packageOverrides?.Values ?? Enumerable.Empty<string>()).ToList();
        var content = files.Keys.Any(k => k.StartsWith("docs/", StringComparison.Ordinal)) ? """ "content": { "root": "docs" }, """ : "";
        var build = TestBuild.Create($$"""
            {
              "version": 1,
              "site": { "basePath": "/docs/" },
              {{content}}
              {{productsJson}}
              "packages": [ {{string.Join(", ", packageEntries)}} ]
            }
            """, packages, directory =>
        {
            foreach (var (path, text) in files)
            {
                directory.CreateTextFile(path, text);
            }

            setup?.Invoke(directory);
        });
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new ContentStage());
        return build;
    }
}
