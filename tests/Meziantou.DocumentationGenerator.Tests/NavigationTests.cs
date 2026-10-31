using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Navigation;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class NavigationTests
{
    // Shaped like meziantou.framework/docs: numbered files and folders, _folder.yml, an images folder and an AGENTS.md
    private static readonly Dictionary<string, string> Pages = new()
    {
        ["docs/AGENTS.md"] = "Instructions, not a page.",
        ["docs/01-Imaging/_folder.yml"] = "title: Imaging\ndescription: Read and write images.",
        ["docs/01-Imaging/01-Introduction.md"] = "---\ntitle: Introduction\ndescription: What the library does.\n---\nText.",
        ["docs/01-Imaging/02-GettingStarted.md"] = "# Getting started\n\nText.",
        ["docs/01-Imaging/10-Formats/_folder.yml"] = "title: Supported formats",
        ["docs/01-Imaging/10-Formats/01-Overview.md"] = "---\ntitle: Formats\n---\nText.",
        ["docs/01-Imaging/10-Formats/02-Png.md"] = "---\ntitle: PNG\n---\nText.",
        ["docs/01-Imaging/03-Guides/_folder.yml"] = "title: Guides",
        ["docs/01-Imaging/03-Guides/01-Overview.md"] = "---\ntitle: Guides\n---\nText.",
        ["docs/01-Imaging/03-Guides/images/logo.txt"] = "not a page",
        ["docs/01-Imaging/11-Limitations.md"] = "No title.",
        ["docs/02-Other/_folder.yml"] = "title: Other\ndescription: Other things.",
        ["docs/02-Other/01-Page.md"] = "---\ntitle: Other page\n---\nText.",
    };

    [Fact]
    public async Task FoldersAndFiles_AreSortedByName()
    {
        await using var build = await BuildAsync();

        var root = build.Context.Navigation.Root;
        Assert.Equal(["Imaging", "Other"], root.Select(n => n.Title));
        var imaging = root[0];
        Assert.True(imaging.IsGroup);
        Assert.Null(imaging.PagePath);
        Assert.Equal(["Introduction", "Getting started", "Guides", "Supported formats", "Limitations"], imaging.Children.Select(n => n.Title));
        Assert.Equal(["Formats", "PNG"], imaging.Children[3].Children.Select(n => n.Title));
        Assert.Equal(["Guides"], imaging.Children[2].Children.Select(n => n.Title));
        Assert.DoesNotContain(build.Context.Content.Pages, p => p.Id.EndsWith("AGENTS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Folders_LinkToTheirFirstPage()
    {
        await using var build = await BuildAsync();

        var imaging = build.Context.Navigation.Root[0];
        Assert.Equal("/docs/imaging/introduction.html", imaging.Url);
        var formats = imaging.Children[3];
        Assert.Equal("Supported formats", formats.Title);
        Assert.Equal("/docs/imaging/supported-formats/formats.html", formats.Url);

        // The first page stays the active page; its folder is only in the trail
        var navigation = build.Context.Navigation.GetPageNavigation("imaging/introduction.html");
        Assert.True(navigation.IsActive(imaging.Children[0]));
        Assert.False(navigation.IsActive(imaging));
        Assert.Equal("/docs/imaging/introduction.html", Assert.Single(navigation.Breadcrumbs, n => n.Title == "Imaging").Url);
    }

    [Fact]
    public async Task Routes_AreComputedFromTitlesAndSlugs()
    {
        await using var build = await BuildAsync(extraFiles: new()
        {
            ["docs/01-Imaging/12-Custom.md"] = "---\ntitle: Ignored for the URL\nslug: my-page\n---\nText.",
            ["docs/01-Imaging/13-Accents.md"] = "---\ntitle: Créer des miniatures & co.\n---\nText.",
        });

        string Route(string id) => build.Context.Content.Pages.Single(p => p.Id == id).Route.FilePath;
        Assert.Equal("imaging/introduction.html", Route("01-Imaging/01-Introduction"));
        Assert.Equal("imaging/getting-started.html", Route("01-Imaging/02-GettingStarted"));
        Assert.Equal("imaging/supported-formats/png.html", Route("01-Imaging/10-Formats/02-Png"));
        Assert.Equal("imaging/limitations.html", Route("01-Imaging/11-Limitations"));
        Assert.Equal("imaging/my-page.html", Route("01-Imaging/12-Custom"));
        Assert.Equal("imaging/creer-des-miniatures-co.html", Route("01-Imaging/13-Accents"));
    }

    [Fact]
    public async Task FolderSlug_ReplacesTheTitle()
    {
        await using var build = await BuildAsync(extraFiles: new() { ["docs/02-Other/_folder.yml"] = "title: Other\nslug: misc" });

        Assert.Equal("misc/other-page.html", build.Context.Content.Pages.Single(p => p.Id == "02-Other/01-Page").Route.FilePath);
    }

    [Fact]
    public async Task InvalidSlug_IsAnError()
    {
        await using var build = await BuildAsync(expectSuccess: false, extraFiles: new() { ["docs/02-Other/01-Page.md"] = "---\ntitle: Other page\nslug: a/b\n---\nText." });

        Assert.Equal("other/other-page.html", build.Context.Content.Pages.Single(p => p.Id == "02-Other/01-Page").Route.FilePath);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5012" && d.Severity == Diagnostics.DiagnosticSeverity.Error && d.Message.Contains("a/b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingFolderMetadata_UsesTheFolderNameWithoutPrefix()
    {
        await using var build = await BuildAsync(extraFiles: new() { ["docs/03-MoreThings/01-Page.md"] = "---\ntitle: Page\n---\nText." });

        Assert.Equal("MoreThings", build.Context.Navigation.Root[2].Title);
        Assert.Equal("morethings/page.html", build.Context.Content.Pages.Single(p => p.Id == "03-MoreThings/01-Page").Route.FilePath);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5011" && d.Location?.FilePath == "docs/03-MoreThings/_folder.yml");
    }

    [Fact]
    public async Task SameRoute_IsAnError()
    {
        await using var build = await BuildAsync(expectSuccess: false, extraFiles: new() { ["docs/02-Other/02-Copy.md"] = "---\ntitle: Other page\n---\nText." });

        Assert.Contains(build.Diagnostics, d => d.Code == "DG4005");
    }

    [Fact]
    public async Task RootIndex_IsTheHomePageAndComesFirst()
    {
        await using var build = await BuildAsync(extraFiles: new() { ["docs/index.md"] = "# Welcome" });

        var root = build.Context.Navigation.Root;
        Assert.Equal(["Welcome", "Imaging", "Other"], root.Select(n => n.Title));
        Assert.Equal("index.html", root[0].PagePath);
    }

    [Fact]
    public async Task ApiReference_IsTheLastSection()
    {
        await using var build = await BuildAsync(packages: [ApiModelTests.CreateKitchenSinkPackage()]);

        var api = build.Context.Navigation.Root[^1];
        Assert.Equal("API reference", api.Title);
        Assert.False(api.IsGroup);
        Assert.Equal("api", api.Kind);
        Assert.Equal("/docs/api/", api.Url);
        Assert.Equal("api/index.html", api.PagePath);

        // Namespaces are the first level, without a package level
        Assert.Equal(["Contoso.Library", "Contoso.Library.Collections"], api.Children.Select(n => n.Title));
        Assert.All(api.Children, n => Assert.Equal("namespace", n.Kind));
        Assert.Equal("/docs/api/namespaces/Contoso.Library.Collections.html", api.Children[1].Url);
        Assert.Equal(["Tree<T>", "Tree<T>.Node", "Tree<T>.Node.Child<TKey>"], api.Children[1].Children.Select(n => n.Title));
    }

    [Fact]
    public async Task ApiReference_MergesNamespacesOfAllPackages()
    {
        var first = new TestPackageBuilder("Contoso.Guard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.Guard.dll", LinkServiceTests.Guard.Value.Assembly);
        var second = new TestPackageBuilder("Contoso.OtherGuard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.OtherGuard.dll", LinkServiceTests.OtherGuard.Value.Assembly);
        await using var build = await BuildAsync(packages: [first, second]);

        var ns = Assert.Single(build.Context.Navigation.Root[^1].Children);
        Assert.Equal("Utils", ns.Title);
        Assert.Equal(["/docs/api/Utils.Guard--Contoso.Guard.html", "/docs/api/Utils.Guard--Contoso.OtherGuard.html"], ns.Children.Select(n => n.Url));
    }

    [Fact]
    public async Task ApiPages_UseTheSiteNavigation()
    {
        await using var build = await BuildAsync(packages: [ApiModelTests.CreateKitchenSinkPackage()]);
        var navigation = build.Context.Navigation.GetPageNavigation("api/Contoso.Library.Client.html");

        Assert.Equal(["Imaging", "Other", "API reference"], navigation.Sidebar.Select(n => n.Title));
        Assert.Equal(["API reference", "Contoso.Library", "Client"], navigation.ActiveTrail.Select(n => n.Title));
        Assert.Equal(["Documentation", "API reference", "Contoso.Library"], navigation.Breadcrumbs.Select(n => n.Title));
        Assert.Null(navigation.Previous);
        Assert.Null(navigation.Next);
    }

    [Fact]
    public async Task PreviousAndNext_FollowTheAuthoredPages()
    {
        await using var build = await BuildAsync(packages: [ApiModelTests.CreateKitchenSinkPackage()]);

        var guides = build.Context.Navigation.GetPageNavigation("imaging/guides/guides.html");
        Assert.Equal("Getting started", guides.Previous?.Title);
        Assert.Equal("Formats", guides.Next?.Title);
        Assert.Equal(["Documentation", "Imaging", "Guides"], guides.Breadcrumbs.Select(n => n.Title));

        var last = build.Context.Navigation.GetPageNavigation("other/other-page.html");
        Assert.Null(last.Next);
    }

    private static async Task<TestBuild> BuildAsync(IReadOnlyList<TestPackageBuilder>? packages = null, bool expectSuccess = true, Dictionary<string, string>? extraFiles = null)
    {
        packages ??= [];
        var files = new Dictionary<string, string>(Pages, StringComparer.Ordinal);
        foreach (var (path, text) in extraFiles ?? [])
        {
            files[path] = text;
        }

        var build = TestBuild.Create($$"""
            {
              "version": 1,
              "site": { "basePath": "/docs/" },
              "content": { "root": "docs", "exclude": [ "**/AGENTS.md" ] },
              "packages": [ {{string.Join(", ", packages.Select(p => TestBuild.PackageReference(p)))}} ]
            }
            """, packages, directory =>
        {
            foreach (var (path, text) in files)
            {
                directory.CreateTextFile(path, text);
            }
        });

        IBuildStage[] stages = [new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new ContentStage(), new NavigationStage()];
        if (expectSuccess)
        {
            await build.RunSuccessfullyAsync(stages);
        }
        else
        {
            await build.RunAsync(stages);
        }

        return build;
    }
}
