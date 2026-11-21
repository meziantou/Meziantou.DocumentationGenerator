using System.Text.RegularExpressions;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.DependencyGraphs;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed partial class DependencyGraphTests
{
    // Contoso.App -> Contoso.Core -> Contoso.Hashing, Contoso.App -> Newtonsoft.Json (not documented), Fabrikam.Tool has no dependency
    private static TestPackageBuilder[] CreatePackages() =>
    [
        Package("Contoso.App", """<group targetFramework="net8.0"><dependency id="Contoso.Core" version="1.0.0" /><dependency id="Newtonsoft.Json" version="13.0.1" /></group>"""),
        Package("Contoso.Core", """<group targetFramework="net8.0"><dependency id="Contoso.Hashing" version="1.0.0" /></group><group targetFramework="netstandard2.0"><dependency id="Contoso.Hashing" version="1.0.0" /><dependency id="System.Memory" version="4.5.5" /></group>"""),
        Package("Contoso.Hashing", dependencies: null),
        Package("Fabrikam.Tool", dependencies: null),
    ];

    [Fact]
    public async Task AllPackages_AreNodesWithLinksAndEdges()
    {
        await using var build = await BuildAsync(CreatePackages(), "<DependencyGraph Packages=\"*\" />");

        var html = GetHtml(build);
        Assert.Equal(["Contoso.App", "Contoso.Core", "Contoso.Hashing", "Fabrikam.Tool"], GetNodeNames(html));
        Assert.Contains("href=\"/docs/api/packages/contoso.core.html\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://www.nuget.org/packages/Fabrikam.Tool/1.0.0\"", html, StringComparison.Ordinal);
        Assert.Contains("<title>Contoso.Core 1.0.0\nDescription of Contoso.Core\nDepends on Contoso.Hashing\nUsed by Contoso.App</title>", html, StringComparison.Ordinal);
        Assert.Equal(["0-1", "1-2"], [.. EdgeRegex().Matches(html).Select(m => m.Groups["edge"].Value)]);
        Assert.DoesNotContain("Newtonsoft.Json", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dependency-graph-edge-partial", html, StringComparison.Ordinal);
        Assert.Contains("<svg class=\"dependency-graph-svg\" role=\"group\" width=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"img\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("xmlns=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dependency-graph-0", html, StringComparison.Ordinal);
        Assert.DoesNotContain(build.Diagnostics, d => d.Code is "DG5013" or "DG5014");
    }

    [Fact]
    public async Task Pattern_IncludesTransitiveDocumentedDependencies()
    {
        await using var build = await BuildAsync(CreatePackages(), "<DependencyGraph Packages=\"contoso.app\" />");

        Assert.Equal(["Contoso.App", "Contoso.Core", "Contoso.Hashing"], GetNodeNames(GetHtml(build)));
    }

    [Fact]
    public async Task External_AddsUndocumentedDependenciesLinkingToNuGetOrg()
    {
        await using var build = await BuildAsync(CreatePackages(), "<DependencyGraph Packages=\"Contoso.*\" External=\"true\" Direction=\"TopDown\" />");

        var html = GetHtml(build);
        Assert.Equal(["Contoso.App", "Contoso.Core", "Contoso.Hashing", "Newtonsoft.Json", "System.Memory"], GetNodeNames(html));
        Assert.Matches("<a href=\"https://www.nuget.org/packages/Newtonsoft.Json\">\\s*<g class=\"node dependency-graph-node dependency-graph-node-external\" data-id=\"n3\">", html);
        Assert.Contains("data-direction=\"top-down\"", html, StringComparison.Ordinal);
        Assert.Contains("dependency-graph-edge dependency-graph-edge-1-4 dependency-graph-edge-partial", html, StringComparison.Ordinal);
        Assert.Contains(">netstandard2.0</text>", html, StringComparison.Ordinal);
        Assert.Contains("A dotted arrow is a dependency of some target frameworks only", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnmatchedPattern_IsAWarning()
    {
        await using var build = await BuildAsync(CreatePackages(), "Intro\n\n<DependencyGraph Packages=\"Contoso.App, Missing.*\" />");

        var warning = Assert.Single(build.Diagnostics, d => d.Code == "DG5014");
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("'Missing.*'", warning.Message, StringComparison.Ordinal);
        Assert.Equal(3, warning.Location!.Line);
        Assert.Contains("dependency-graph-node", GetHtml(build), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoPackage_IsAWarningAndAnEmptyFigure()
    {
        await using var build = await BuildAsync([], "<DependencyGraph Packages=\"*\" />");

        Assert.Contains("No package matches this dependency graph.", GetHtml(build), StringComparison.Ordinal);
        Assert.Contains("documents no package", Assert.Single(build.Diagnostics, d => d.Code == "DG5014").Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<DependencyGraph />", "requires the 'Packages' attribute")]
    [InlineData("<DependencyGraph Packages=\" \" />", "requires the 'Packages' attribute")]
    [InlineData("<DependencyGraph Packages=\"A,,B\" />", "invalid package pattern ''")]
    [InlineData("<DependencyGraph Packages=\"a b\" />", "invalid package pattern 'a b'")]
    [InlineData("<DependencyGraph Packages=\"*\" External=\"maybe\" />", "invalid 'External' value 'maybe'")]
    [InlineData("<DependencyGraph Packages=\"*\" Direction=\"1\" />", "invalid 'Direction' value '1'")]
    [InlineData("<DependencyGraph Packages=\"*\" Id=\"A\" />", "does not support the attribute 'Id'")]
    [InlineData("Text <DependencyGraph Packages=\"*\" /> here.", "must be on its own line")]
    public async Task InvalidElements_AreErrors(string markdown, string message)
    {
        await using var build = await BuildAsync(CreatePackages(), markdown);

        Assert.DoesNotContain("dependency-graph", GetHtml(build), StringComparison.Ordinal);
        var error = Assert.Single(build.Diagnostics, d => d.Code == "DG5013");
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthoredMarkup_IsStillSanitized()
    {
        await using var build = await BuildAsync(CreatePackages(), """
            <svg onload="alert(1)"><a href="javascript:alert(1)">x</a></svg>

            <div class="dependency-graph" data-node="1" onclick="alert(1)">fake</div>

            <p>dependency-graph-00000000000000000000000000000000</p>

            <DependencyGraph Packages="Fabrikam.Tool" />
            """);

        var html = GetHtml(build);
        Assert.DoesNotContain("onload", html, StringComparison.Ordinal);
        Assert.DoesNotContain("onclick", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-node=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("dependency-graph-00000000000000000000000000000000", html, StringComparison.Ordinal);
        Assert.Single(GetNodeNames(html));
    }

    [Fact]
    public async Task Site_RendersGraphWithRelativeLinks()
    {
        await using var site = await SiteBuilder.BuildAsync(CreatePackages(), files: new()
        {
            ["docs/guides/packages.md"] = "# Packages\n\n<DependencyGraph Packages=\"*\" />",
        });

        Assert.Equal(0, site.Result.ExitCode);
        Assert.DoesNotContain("DG4003", site.Result.AllOutput, StringComparison.Ordinal);
        var html = site.ReadFile("guides/packages.html");
        Assert.Contains("href=\"../api/packages/contoso.core.html\"", html, StringComparison.Ordinal);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        Assert.Contains("<text", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*", "Anything", true)]
    [InlineData("Contoso.*", "contoso.core", true)]
    [InlineData("Contoso.*", "Contoso", false)]
    [InlineData("*.Core", "Contoso.Core", true)]
    [InlineData("C*o*e", "Contoso.Core", true)]
    [InlineData("C*o*e", "Contoso.Cor", false)]
    [InlineData("Contoso.Core", "Contoso.Core.Extra", false)]
    public void IsMatch(string pattern, string text, bool expected) => Assert.Equal(expected, DependencyGraphBuilder.IsMatch(pattern, text));

    [Fact]
    public void Svg_CyclesAndIsolatedPackagesAreDrawn()
    {
        var graph = CreateGraph(4, [(0, 1), (1, 2), (2, 0)]);

        var svg = DependencyGraphSvgWriter.WriteSvg(graph, DependencyGraphDirection.LeftRight, "summary");

        Assert.Equal(["0-1", "1-2", "2-0"], [.. EdgeRegex().Matches(svg).Select(m => m.Groups["edge"].Value)]);
        Assert.Equal(["Package0", "Package1", "Package2", "Package3"], GetNodeNames(svg));
        Assert.Equal(svg, DependencyGraphSvgWriter.WriteSvg(graph, DependencyGraphDirection.LeftRight, "summary"));
    }

    [Fact]
    public void Svg_LargeGraph_IsFast()
    {
        // Pseudo-random edges, from a fixed formula so the test is deterministic
        var edges = Enumerable.Range(0, 600).Select(i => (i * 37 % 200, ((i * 91) + (i / 200 * 13)) % 200)).Where(e => e.Item1 != e.Item2).Distinct().ToList();
        var graph = CreateGraph(200, edges);

        var start = System.Diagnostics.Stopwatch.StartNew();
        var svg = DependencyGraphSvgWriter.WriteSvg(graph, DependencyGraphDirection.TopDown, "summary");

        Assert.Equal(edges.Count, EdgeRegex().Count(svg));
        Assert.InRange(start.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    private static DependencyGraphModel CreateGraph(int count, IEnumerable<(int From, int To)> edges) => new(
        [.. Enumerable.Range(0, count).Select(i => new DependencyGraphNode("Package" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "1.0.0", Description: null, "https://example.com/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), IsExternal: false))],
        [.. edges.Select(e => new DependencyGraphEdge(e.From, e.To, "(>= 1.0.0)", []))],
        []);

    private static readonly Dictionary<string, Lazy<CompiledAssembly>> Assemblies = new(StringComparer.Ordinal)
    {
        ["Contoso.App"] = new(() => TestCompiler.Compile("Contoso.App", "namespace Contoso.App; public static class Program { }")),
        ["Contoso.Core"] = new(() => TestCompiler.Compile("Contoso.Core", "namespace Contoso.Core; public static class Core { }")),
        ["Contoso.Hashing"] = new(() => TestCompiler.Compile("Contoso.Hashing", "namespace Contoso.Hashing; public static class Hash { }")),
    };

    // Fabrikam.Tool has no assembly, so it has no page and links to nuget.org
    private static TestPackageBuilder Package(string id, string? dependencies)
    {
        var builder = new TestPackageBuilder(id, "1.0.0");
        builder = Assemblies.TryGetValue(id, out var assembly)
            ? builder.AddFile($"lib/net8.0/{id}.dll", assembly.Value.Assembly).AddFile($"lib/net8.0/{id}.xml", assembly.Value.Xml)
            : builder.AddFile("tools/readme.txt", id);
        return dependencies is null ? builder : builder.WithNuspecMetadata("<dependencies>" + dependencies + "</dependencies>");
    }

    private static string GetHtml(TestBuild build) => build.Context.Content.Pages.Single().Rendered!.Html;

    private static List<string> GetNodeNames(string html) => [.. NodeNameRegex().Matches(html).Select(m => m.Groups["name"].Value)];

    private static async Task<TestBuild> BuildAsync(IReadOnlyList<TestPackageBuilder> packages, string markdown)
    {
        var packageEntries = packages.Select(p => TestBuild.PackageReference(p)).ToList();
        var build = TestBuild.Create($$"""
            {
              "version": 1,
              "site": { "basePath": "/docs/" },
              "content": { "root": "docs" },
              "packages": [ {{string.Join(", ", packageEntries)}} ]
            }
            """, packages, directory => directory.CreateTextFile("docs/index.md", markdown));
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new ContentStage());
        return build;
    }

    [GeneratedRegex("<g class=\"node dependency-graph-node[^\"]*\" data-id=\"n[0-9]+\">.*?<text[^>]*>(?<name>[^<]+)</text>", RegexOptions.ExplicitCapture | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NodeNameRegex();

    [GeneratedRegex("class=\"edge [a-z]+ dependency-graph-edge dependency-graph-edge-(?<edge>[0-9]+-[0-9]+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EdgeRegex();
}
