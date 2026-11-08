using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

/// <summary>Tests of the <c>&lt;SectionList /&gt;</c> and <c>&lt;PackageList /&gt;</c> Markdown components.</summary>
public sealed class EntryListComponentTests
{
    private static readonly Dictionary<string, string> SectionFiles = new(StringComparer.Ordinal)
    {
        ["docs/01-guide/_folder.yml"] = "title: Guide\ndescription: Learn the basics\n",
        ["docs/01-guide/01-intro.md"] = "# Introduction\n\nIntro text.",
        ["docs/02-faq.md"] = "---\ndescription: Common <questions>\n---\n# FAQ\n\nText.",
    };

    [Fact]
    public async Task SectionList_DefaultLayout_IsTheGeneratedHomePageCards()
    {
        await using var build = await BuildAsync([], SectionFiles, "Welcome\n\n<SectionList />");

        var html = GetHtml(build, "index");
        Assert.Contains("<ul class=\"section-catalog\">", html, StringComparison.Ordinal);
        Assert.Contains("<li class=\"section-card\"><h3><a href=\"/docs/guide/introduction.html\">Guide</a></h3><p>Learn the basics</p></li>", html, StringComparison.Ordinal);
        Assert.Contains("<li class=\"section-card\"><h3><a href=\"/docs/faq.html\">FAQ</a></h3><p>Common &lt;questions&gt;</p></li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("component-", html, StringComparison.Ordinal);
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG5013");
    }

    [Fact]
    public async Task SectionList_UsesTitlesOfPagesRenderedLater_AndSkipsTheHomePage()
    {
        // 01-guide/01-intro.md is rendered before 02-faq.md, whose title and description come from its content
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["docs/01-guide/_folder.yml"] = "title: Guide\n",
            ["docs/01-guide/01-intro.md"] = "# Introduction\n\n<SectionList Layout=\"List\" />",
            ["docs/02-faq.md"] = "# Frequently asked questions\n\nAnswers to common questions.",
        };
        await using var build = await BuildAsync([], files, "# Welcome\n\n<SectionList Layout=\"List\" />");

        foreach (var pageId in new[] { "01-guide/01-intro", "index" })
        {
            var html = GetHtml(build, pageId);
            Assert.Contains("<li><a href=\"/docs/frequently-asked-questions.html\">Frequently asked questions</a> <span class=\"entry-list-description\">Answers to common questions.</span></li>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Welcome", html, StringComparison.Ordinal);
            Assert.DoesNotContain("component-", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SectionList_ListAndTableLayouts()
    {
        await using var build = await BuildAsync([], SectionFiles, "<SectionList Layout=\"list\" />\n\n<SectionList Layout=\"Table\" />");

        var html = GetHtml(build, "index");
        Assert.Contains("<ul class=\"entry-list\"><li><a href=\"/docs/guide/introduction.html\">Guide</a> <span class=\"entry-list-description\">Learn the basics</span></li>", html, StringComparison.Ordinal);
        Assert.Contains("<table class=\"api-table\"><thead><tr><th scope=\"col\">Name</th><th scope=\"col\">Description</th></tr></thead>", html, StringComparison.Ordinal);
        Assert.Contains("<tr><td><a href=\"/docs/faq.html\">FAQ</a></td><td>Common &lt;questions&gt;</td></tr>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageList_ListsPackagesWithAnApi()
    {
        await using var build = await BuildAsync(CreatePackages(), [], "<PackageList />");

        var html = GetHtml(build, "index");
        Assert.Contains("<li class=\"section-card\"><h3><a href=\"/docs/api/packages/contoso.core.html\">Contoso.Core</a></h3><p class=\"section-card-version\">1.0.0</p><p>Description of Contoso.Core</p></li>", html, StringComparison.Ordinal);
        Assert.Contains("Fabrikam.Json", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Fabrikam.Tool", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("Contoso.Core", StringComparison.Ordinal) < html.IndexOf("Fabrikam.Json", StringComparison.Ordinal));
        Assert.DoesNotContain(build.Diagnostics, d => d.Code is "DG5013" or "DG5015");
    }

    [Fact]
    public async Task PackageList_PatternsAndTableLayout_FromANestedPage()
    {
        var files = new Dictionary<string, string>(SectionFiles, StringComparer.Ordinal)
        {
            ["docs/01-guide/02-packages.md"] = "# Packages\n\n<PackageList Packages=\"contoso.*\" Layout=\"Table\" />",
        };
        await using var build = await BuildAsync(CreatePackages(), files, "Home");

        var html = GetHtml(build, "01-guide/02-packages");
        Assert.Contains("<th scope=\"col\">Package</th><th scope=\"col\">Version</th><th scope=\"col\">Description</th>", html, StringComparison.Ordinal);
        Assert.Contains("<tr><td><a href=\"/docs/api/packages/contoso.core.html\">Contoso.Core</a></td><td>1.0.0</td><td>Description of Contoso.Core</td></tr>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Fabrikam", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageList_UnmatchedPattern_IsAWarning()
    {
        await using var build = await BuildAsync(CreatePackages(), [], "Intro\n\n<PackageList Packages=\"Contoso.*, Missing.*\" Layout=\"List\" />");

        var warning = Assert.Single(build.Diagnostics, d => d.Code == "DG5015");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("'Missing.*'", warning.Message, StringComparison.Ordinal);
        Assert.Equal(3, warning.Location!.Line);
        Assert.Contains("<ul class=\"entry-list\"><li><a href=\"/docs/api/packages/contoso.core.html\">Contoso.Core</a> <span class=\"entry-list-version\">1.0.0</span>", GetHtml(build, "index"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageList_NoPackage_IsAWarningAndRendersNothing()
    {
        await using var build = await BuildAsync([], [], "Before\n\n<PackageList />\n\nAfter");

        Assert.Equal("<p>Before</p>\n\n<p>After</p>\n", GetHtml(build, "index"));
        Assert.Contains("documents no package", Assert.Single(build.Diagnostics, d => d.Code == "DG5015").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidElements_AreErrors()
    {
        await using var build = await BuildAsync([], [], """
            <SectionList Layout="Grid" />

            <PackageList Packages="a b" />

            <PackageList Foo="bar" />

            Text <SectionList /> here.
            """);

        var errors = build.Diagnostics.Where(d => d.Code == "DG5013").ToList();
        Assert.Equal([1, 3, 5, 7], errors.Select(d => d.Location!.Line));
        Assert.Contains(errors, d => d.Message.Contains("invalid 'Layout' value 'Grid'; expected Cards, List or Table", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Message.Contains("invalid package pattern 'a b'", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Message.Contains("must be on its own line", StringComparison.Ordinal));
        var html = GetHtml(build, "index");
        Assert.DoesNotContain("section-catalog", html, StringComparison.Ordinal);
        Assert.DoesNotContain("api-table", html, StringComparison.Ordinal);
    }

    private static readonly Dictionary<string, Lazy<CompiledAssembly>> Assemblies = new(StringComparer.Ordinal)
    {
        ["Contoso.Core"] = new(() => TestCompiler.Compile("Contoso.Core", "namespace Contoso.Core; public static class Core { }")),
        ["Fabrikam.Json"] = new(() => TestCompiler.Compile("Fabrikam.Json", "namespace Fabrikam.Json; public static class Json { }")),
    };

    // Fabrikam.Tool has no assembly, so it has no public API and is not listed
    private static TestPackageBuilder[] CreatePackages() => [Package("Fabrikam.Json"), Package("Contoso.Core"), Package("Fabrikam.Tool")];

    private static TestPackageBuilder Package(string id)
    {
        var builder = new TestPackageBuilder(id, "1.0.0");
        return Assemblies.TryGetValue(id, out var assembly)
            ? builder.AddFile($"lib/net8.0/{id}.dll", assembly.Value.Assembly).AddFile($"lib/net8.0/{id}.xml", assembly.Value.Xml)
            : builder.AddFile("tools/readme.txt", id);
    }

    private static string GetHtml(TestBuild build, string pageId) => build.Context.Content.Pages.Single(p => p.Id == pageId).Rendered!.Html;

    private static async Task<TestBuild> BuildAsync(IReadOnlyList<TestPackageBuilder> packages, Dictionary<string, string> files, string homeMarkdown)
    {
        var packageEntries = packages.Select(p => TestBuild.PackageReference(p)).ToList();
        var build = TestBuild.Create($$"""
            {
              "version": 1,
              "site": { "basePath": "/docs/" },
              "content": { "root": "docs" },
              "packages": [ {{string.Join(", ", packageEntries)}} ]
            }
            """, packages, directory =>
        {
            directory.CreateTextFile("docs/index.md", homeMarkdown);
            foreach (var (path, text) in files)
            {
                directory.CreateTextFile(path, text);
            }
        });
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new ContentStage());
        return build;
    }
}
