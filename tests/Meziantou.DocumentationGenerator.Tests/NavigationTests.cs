using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Navigation;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class NavigationTests
{
    private static readonly Dictionary<string, string> Pages = new()
    {
        ["docs/index.md"] = "# Home",
        ["docs/intro.md"] = "---\ntitle: Front matter title\nuid: intro-page\n---\n# Heading",
        ["docs/guides/first.md"] = "# First guide",
        ["docs/guides/second.md"] = "No heading here.",
        ["docs/guides/toc.yml"] = "- href: first.md\n- href: second.md\n- name: Back\n  href: ../intro.md#section",
        ["docs/orphan.md"] = "# Orphan",
    };

    [Fact]
    public async Task SequenceRoot_NestedTocsAndTitles()
    {
        await using var build = await BuildAsync("""
            - name: Home
              href: index.md
            - href: intro.md
            - name: Guides
              href: guides/
            - name: Group
              expanded: true
              items:
                - name: External
                  href: https://example.com/
                - uid: intro-page
            """);

        var root = build.Context.Navigation.Root;
        Assert.Equal(["Home", "Front matter title", "Guides", "Group"], root.Select(n => n.Title));
        var guides = root[2];
        Assert.Equal("/docs/guides/first.html", guides.Url);
        Assert.Null(guides.PagePath);
        Assert.Equal(["First guide", "second", "Back"], guides.Children.Select(n => n.Title));
        Assert.Equal("/docs/intro.html#section", guides.Children[2].Url);

        var group = root[3];
        Assert.Null(group.Url);
        Assert.True(group.IsExpanded);
        Assert.True(group.Children[0].IsExternal);
        Assert.Equal("Front matter title", group.Children[1].Title);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5005" && d.Location?.FilePath == "docs/orphan.md");
    }

    [Fact]
    public async Task ItemsRoot_DirectoryReferenceWithoutName()
    {
        await using var build = await BuildAsync("""
            items:
              - href: guides/toc.yml
            """);

        Assert.Equal(["First guide", "second", "Back"], build.Context.Navigation.Root.Select(n => n.Title));
    }

    [Fact]
    public async Task ApiInsertionPoints()
    {
        await using var build = await BuildAsync("""
            - name: Reference
              href: api:contoso.library
            - href: api:contoso.library
            """, packages: [ApiModelTests.CreateKitchenSinkPackage()]);

        var reference = build.Context.Navigation.Root[0];
        Assert.Equal("/docs/contoso.library/api/", reference.Url);
        Assert.Equal(["Contoso.Library", "Contoso.Library.Collections"], reference.Children.Select(n => n.Title));
        Assert.Equal(["Tree<T>", "Tree<T>.Node", "Tree<T>.Node.Child<TKey>"], reference.Children[1].Children.Select(n => n.Title));
        Assert.Equal(["Contoso.Library", "Contoso.Library.Collections"], build.Context.Navigation.Root.Skip(1).Select(n => n.Title));
    }

    [Fact]
    public async Task ApiUid()
    {
        await using var build = await BuildAsync("- uid: T:Contoso.Library.Client\n- name: Custom\n  uid: Contoso.Library.Message", packages: [ApiModelTests.CreateKitchenSinkPackage()]);

        Assert.Equal(["Client", "Custom"], build.Context.Navigation.Root.Select(n => n.Title));
        Assert.Equal("/docs/contoso.library/api/Contoso.Library.Client.html", build.Context.Navigation.Root[0].Url);
    }

    [Fact]
    public async Task PageInSeveralTocs_UsesTheFirstOccurrence()
    {
        await using var build = await BuildAsync("""
            - name: A
              items:
                - href: intro.md
            - name: B
              items:
                - href: intro.md
            """);

        var navigation = build.Context.Navigation.GetPageNavigation("intro.html", productId: null);
        Assert.Equal(["A", "Front matter title"], navigation.ActiveTrail.Select(n => n.Title));
        Assert.Same(build.Context.Navigation.Root[0], navigation.ActiveTrail[0]);
        Assert.Equal(["Home", "A"], navigation.Breadcrumbs.Select(n => n.Title));
    }

    [Fact]
    public async Task PreviousAndNext()
    {
        await using var build = await BuildAsync("- href: index.md\n- href: intro.md\n- href: guides/toc.yml");

        var navigation = build.Context.Navigation.GetPageNavigation("intro.html", productId: null);
        Assert.Equal("Home", navigation.Previous?.Title);
        Assert.Equal("First guide", navigation.Next?.Title);
    }

    [Fact]
    public async Task ProductPages_UseTheGeneratedTreeWhenNotInTheToc()
    {
        await using var build = await BuildAsync("- href: intro.md", packages: [ApiModelTests.CreateKitchenSinkPackage()]);
        var navigation = build.Context.Navigation.GetPageNavigation("contoso.library/api/Contoso.Library.Client.html", "contoso.library");

        Assert.Equal(["Overview", "API reference"], navigation.Sidebar.Select(n => n.Title));
        Assert.Equal(["API reference", "Contoso.Library", "Client"], navigation.ActiveTrail.Select(n => n.Title));
        Assert.Equal(["Home", "API reference", "Contoso.Library"], navigation.Breadcrumbs.Select(n => n.Title));
        Assert.Equal(["Contoso.Library"], build.Context.Navigation.Header.Select(n => n.Title));
    }

    [Fact]
    public async Task InvalidTocs_AreReportedWithLocations()
    {
        await using var build = await BuildAsync("""
            - name: Missing
              href: missing.md
            - name: Script
              href: javascript:alert(1)
            - name: Product
              href: api:unknown
            - name: Unknown key
              href: intro.md
              displayName: ignored
            - name: Outside
              href: ../secret.md
            - uid: does-not-exist
            """, expectSuccess: false);

        var diagnostics = build.Diagnostics;
        Assert.Contains(diagnostics, d => d.Code == "DG5006" && d.Location?.FilePath == "docs/toc.yml" && d.Location.Line == 1);
        Assert.Contains(diagnostics, d => d.Code == "DG5003" && d.Message.Contains("javascript", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG5006" && d.Message.Contains("unknown", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG5007" && d.Message.Contains("displayName", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG5009");
        Assert.Contains(diagnostics, d => d.Code == "DG5006" && d.Message.Contains("does-not-exist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cycles_AreDetected()
    {
        await using var build = await BuildAsync("- href: a/toc.yml", expectSuccess: false, extraFiles: new()
        {
            ["docs/a/toc.yml"] = "- href: ../b/toc.yml",
            ["docs/b/toc.yml"] = "- href: ../a/toc.yml",
        });

        var diagnostic = Assert.Single(build.Diagnostics, d => d.Code == "DG5004");
        Assert.Contains("docs/a/toc.yml -> docs/b/toc.yml -> docs/a/toc.yml", diagnostic.Message);
    }

    [Fact]
    public async Task AliasesAreRejected()
    {
        await using var build = await BuildAsync("- &a\n  name: A\n  href: intro.md\n- *a", expectSuccess: false);

        Assert.Contains(build.Diagnostics, d => d.Code == "DG5003");
    }

    [Fact]
    public async Task WithoutToc_DefaultNavigation()
    {
        await using var build = await BuildAsync(toc: null, packages: [ApiModelTests.CreateKitchenSinkPackage()]);

        Assert.Equal(["Home", "First guide", "second", "Front matter title", "Orphan", "Contoso.Library"], build.Context.Navigation.Root.Select(n => n.Title).ToArray()[..6]);
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG5005");
    }

    private static async Task<TestBuild> BuildAsync(string? toc, IReadOnlyList<TestPackageBuilder>? packages = null, bool expectSuccess = true, Dictionary<string, string>? extraFiles = null)
    {
        packages ??= [];
        var build = TestBuild.Create($$"""
            {
              "version": 1,
              "site": { "basePath": "/docs/" },
              "content": { "root": "docs" },
              "packages": [ {{string.Join(", ", packages.Select(p => TestBuild.PackageReference(p)))}} ]
            }
            """, packages, directory =>
        {
            foreach (var (path, text) in Pages.Concat(extraFiles ?? []))
            {
                directory.CreateTextFile(path, text);
            }

            if (toc is not null)
            {
                directory.CreateTextFile("docs/toc.yml", toc);
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
