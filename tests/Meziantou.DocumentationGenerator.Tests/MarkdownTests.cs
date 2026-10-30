using System.Text.RegularExpressions;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed partial class MarkdownTests
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
        Assert.DoesNotContain("title:", index.Rendered.Html);
        Assert.DoesNotContain("<pre", index.Rendered.Html);
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
    public async Task EditUrl_LinksEveryPageToItsSource()
    {
        await using var site = await SiteBuilder.BuildAsync([], files: new()
        {
            ["pages/index.md"] = "---\ntitle: Home\n---\nText.",
            ["pages/01-Guides/_folder.yml"] = "title: Guides",
            ["pages/01-Guides/01-Install.md"] = "# Install",
        }, extraConfiguration: """ "content": { "root": "pages", "editUrl": "https://github.com/contoso/docs/edit/main/pages/{path}" }, """);

        Assert.Equal(0, site.Result.ExitCode);
        var home = HtmlDocumentLoader.Parse(site.ReadFile("index.html")).QuerySelector("article .heading-row > a.edit-link")!;
        Assert.Equal("https://github.com/contoso/docs/edit/main/pages/index.md", home.GetAttribute("href"));
        Assert.Equal("Edit this page on GitHub", home.GetAttribute("aria-label"));
        Assert.Equal("edit", home.TextContent);
        var install = HtmlDocumentLoader.Parse(site.ReadFile("guides/install.html")).QuerySelector("a.edit-link")!;
        Assert.Equal("https://github.com/contoso/docs/edit/main/pages/01-Guides/01-Install.md", install.GetAttribute("href"));
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
        Assert.Contains("""<a href="/docs/api/contoso.library/Contoso.Library.Client.html"><code>Client</code></a>""", html);
        Assert.Contains("""<a href="/docs/api/contoso.library/Contoso.Library.Client.html#Send(System.String)">the send method</a>""", html);
        Assert.Contains("""<a href="/docs/api/contoso.library/Contoso.Library.Collections.Tree-1.html"><code>Tree&lt;T&gt;</code></a>""", html);
        Assert.Contains("<code>Type</code>", html);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4001" && d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Missing.Type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AmbiguousCref_IsAnError()
    {
        var first = new TestPackageBuilder("Contoso.Guard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.Guard.dll", LinkServiceTests.Guard.Value.Assembly);
        var second = new TestPackageBuilder("Contoso.OtherGuard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.OtherGuard.dll", LinkServiceTests.OtherGuard.Value.Assembly);
        await using var build = await BuildAsync(packages: [first, second], files: new()
        {
            ["docs/index.md"] = "[](cref:T:Utils.Guard) [qualified](cref:Contoso.Guard!T:Utils.Guard)",
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.Contains("""<a href="/docs/api/contoso.guard/Utils.Guard.html">qualified</a>""", html);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4002" && d.Severity == DiagnosticSeverity.Error && d.Message.Contains("'T:Utils.Guard' is ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AmbiguousNamespaceCref_IsReportedAsAmbiguous()
    {
        var first = new TestPackageBuilder("Contoso.Guard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.Guard.dll", LinkServiceTests.Guard.Value.Assembly);
        var second = new TestPackageBuilder("Contoso.OtherGuard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.OtherGuard.dll", LinkServiceTests.OtherGuard.Value.Assembly);
        await using var build = await BuildAsync(packages: [first, second], files: new()
        {
            ["docs/index.md"] = "[](cref:Utils) [qualified](cref:Contoso.Guard!Utils)",
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.Contains("""<a href="/docs/api/contoso.guard/namespaces/Utils.html">qualified</a>""", html);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4002" && d.Message.Contains("'Utils' is ambiguous", StringComparison.Ordinal));
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG4001");
    }

    [Fact]
    public async Task CrefWithoutPackages_ExplainsThereIsNoApiReference()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = "[](cref:Contoso.Library)",
        });

        Assert.Contains(build.Diagnostics, d => d.Code == "DG4001" && d.Message.Contains("no package is configured", StringComparison.Ordinal));
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
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4008" && d.Severity == DiagnosticSeverity.Error);
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
    public async Task InstallComponents_AreReplacedByCommands()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = """
                Install:

                <InstallNuGetPackage Id="Meziantou.Framework" />

                <InstallNuGetPackage Id="Meziantou.Framework" Version="1.2.3" />
                <installdotnettool id='Meziantou.Tool' />
                <InstallDotnetTool Id="Meziantou.Tool" Version="2.0.0-beta.1" Local="true" />

                Or run <InstallNuGetPackage Id="Inline.Package" /> in a terminal.

                <details>
                <summary>More</summary>
                </details>
                """,
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.Equal(4, Regex.Count(html, """<pre><code class="hljs language-bash">"""));
        var text = HtmlTagRegex().Replace(html, "");
        Assert.Contains("dotnet package add Meziantou.Framework\n", text);
        Assert.Contains("dotnet package add Meziantou.Framework --version 1.2.3\n", text);
        Assert.Contains("dotnet tool install --global Meziantou.Tool\n", text);
        Assert.Contains("dotnet tool install Meziantou.Tool --version 2.0.0-beta.1\n", text);
        Assert.Contains("Or run <code>dotnet package add Inline.Package</code> in a terminal.", html);
        Assert.Contains("<details>", html);
        Assert.DoesNotContain("<InstallNuGetPackage", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<InstallDotnetTool", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG5013");
    }

    [Fact]
    public async Task Components_FollowMarkdownStructure()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = """
                - Step one:

                  <InstallNuGetPackage Id="In.List" />

                > <NuGetReference Id="In.Quote" />

                Literal `<InstallNuGetPackage Id="In.Code" />` text.

                <!-- <InstallNuGetPackage Id="In.Comment" /> -->

                ```markdown
                <InstallNuGetPackage Id="In.Fence" />
                ```
                """,
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.Matches("""<li>\s*<p>Step one:</p>\s*<pre><code class="hljs language-bash">dotnet package add In.List""", html);
        Assert.Matches("""<blockquote>\s*<p><a href="https://www.nuget.org/packages/In.Quote" class="nuget-reference">In.Quote</a></p>""", html);
        Assert.Contains("""<code>&lt;InstallNuGetPackage Id="In.Code" /&gt;</code>""", html);
        Assert.DoesNotContain("In.Comment", html);
        Assert.Contains("In.Fence", html);
        Assert.DoesNotContain("dotnet package add In.Fence", html);
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG5013");
    }

    [Fact]
    public async Task NuGetReference_IsALinkToNuGetOrg()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = """
                See <NuGetReference Id="Meziantou.Framework" /> for details.

                <NuGetReference Id="Meziantou.Framework" Version="1.2.3" />
                <NuGetReference Id="Other.Package" />
                """,
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.Contains("""<p>See <a href="https://www.nuget.org/packages/Meziantou.Framework" class="nuget-reference">Meziantou.Framework</a> for details.</p>""", html);
        Assert.Contains("""<p><a href="https://www.nuget.org/packages/Meziantou.Framework/1.2.3" class="nuget-reference">Meziantou.Framework</a></p>""", html);
        Assert.Contains("""<p><a href="https://www.nuget.org/packages/Other.Package" class="nuget-reference">Other.Package</a></p>""", html);
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG5013");
    }

    [Fact]
    public async Task InvalidComponents_AreErrors()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/index.md"] = """
                <InstallNuGetPackage />

                <InstallNuGetPackage Id="a b" />

                <InstallNuGetPackage Id="Valid" Foo="bar" />

                <InstallNuGetPackage Id="Valid" Version="not-a-version" />

                <InstallDotnetTool Id="Valid" Local="maybe" />

                Text <NuGetReference Version="1.0.0" /> here.
                """,
        });

        var html = build.Context.Content.Pages.Single().Rendered!.Html;
        Assert.DoesNotContain("<pre", html);
        Assert.DoesNotContain("nuget-reference", html);
        Assert.Contains("<p>Text  here.</p>", html);
        var errors = build.Diagnostics.Where(d => d.Code == "DG5013").ToList();
        Assert.All(errors, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Equal([1, 3, 5, 7, 9, 11], errors.Select(d => d.Location!.Line));
        Assert.Contains(errors, d => d.Message.Contains("requires the 'Id' attribute", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Message.Contains("invalid package identifier 'a b'", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Message.Contains("does not support the attribute 'Foo'", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Message.Contains("invalid version 'not-a-version'", StringComparison.Ordinal));
        Assert.All(errors, d => Assert.Equal("docs/index.md", d.Location!.FilePath));
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
    public async Task FoldersWithTheSameSlug_AreAnError()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/01-Guides/_folder.yml"] = "title: Guides",
            ["docs/01-Guides/01-First.md"] = "# First",
            ["docs/02-Guides/_folder.yml"] = "title: guides",
            ["docs/02-Guides/01-Second.md"] = "# Second",
        });

        Assert.Contains(build.Diagnostics, d => d.Code == "DG4005" && d.Severity == DiagnosticSeverity.Error && d.Location?.FilePath == "docs/02-Guides/_folder.yml" && d.Message.Contains("'02-Guides' and '01-Guides'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PageMappedToTheHomePage_IsAnError()
    {
        await using var build = await BuildAsync(files: new()
        {
            ["docs/start.md"] = "---\nslug: index\n---\n# Start",
        });

        Assert.Null(build.Context.Content.HomePage);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4005" && d.Severity == DiagnosticSeverity.Error && d.Location?.FilePath == "docs/start.md" && d.Message.Contains("reserved for the home page", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingFragment_IsReportedOnTheMarkdownFile()
    {
        await using var site = await SiteBuilder.BuildAsync([], files: new()
        {
            ["docs/index.md"] = "[other](other.md#missing) [self](#nope)",
            ["docs/other.md"] = "# Other\n\n## Sample",
        });

        Assert.Equal(1, site.Result.ExitCode);
        Assert.Contains("docs/index.md: error DG4003: Broken link 'other.html#missing' in 'index.html': 'other.html' has no element with the identifier 'missing'", site.Result.AllOutput, StringComparison.Ordinal);
        Assert.Contains("docs/index.md: error DG4003: Broken link '#nope' in 'index.html'", site.Result.AllOutput, StringComparison.Ordinal);
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
        Assert.DoesNotContain(build.Context.Links!.Routes, r => r.FilePath.StartsWith("contoso.readme/", StringComparison.Ordinal) && !r.FilePath.StartsWith("api/contoso.readme/", StringComparison.Ordinal));
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

    [GeneratedRegex("<[^>]+>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HtmlTagRegex();
}
