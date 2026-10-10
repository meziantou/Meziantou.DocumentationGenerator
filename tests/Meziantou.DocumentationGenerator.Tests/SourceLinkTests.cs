using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.SourceLinks;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class SourceLinkTests
{
    internal const string Commit = "0123456789abcdef0123456789abcdef01234567";
    internal const string GitHubSourceLink = """{"documents":{"/_/*":"https://raw.githubusercontent.com/contoso/library/0123456789abcdef0123456789abcdef01234567/*"}}""";

    private const string WidgetSource = """
        namespace Contoso.Sources
        {
            public class Widget
            {
                public int Value { get; set; }

                public int Compute(int x)
                {
                    return x * 2;
                }

                public async System.Threading.Tasks.Task<int> ComputeAsync()
                {
                    await System.Threading.Tasks.Task.Yield();
                    return 1;
                }

                public const int Constant = 1;
            }
        }
        """;

    private const string GeneratedSource = """
        namespace Contoso.Sources
        {
            public class Generated
            {
                public void Run()
                {
                }
            }
        }
        """;

    private static CompiledAssembly Compile(string? sourceLink = GitHubSourceLink, bool embedded = false, string widgetSource = WidgetSource, bool referenceAssembly = false, string[]? defines = null)
    {
        return TestCompiler.Compile("Contoso.Sources", [("/_/src/Widget.cs", widgetSource), ("/generated/Generated.g.cs", GeneratedSource)], new CompileOptions { SourceLink = sourceLink, EmbeddedPdb = embedded, ReferenceAssembly = referenceAssembly, Defines = defines ?? [] });
    }

    [Fact]
    public async Task AdjacentPdb_MembersAndTypes()
    {
        var assembly = Compile();
        var package = CreatePackage(p => p.AddFile("lib/net8.0/Contoso.Sources.dll", assembly.Assembly).AddFile("lib/net8.0/Contoso.Sources.pdb", assembly.Pdb!));
        await using var build = await BuildAsync(package);

        var baseUrl = $"https://github.com/contoso/library/blob/{Commit}/src/Widget.cs";
        Assert.Equal(new SourceLocation(baseUrl + "#L8", SourcePrecision.Line, "src/Widget.cs", 8), GetLocation(build, "M:Contoso.Sources.Widget.Compute(System.Int32)"));
        Assert.Equal(new SourceLocation(baseUrl, SourcePrecision.File, "src/Widget.cs", null), GetLocation(build, "T:Contoso.Sources.Widget"));
        Assert.Equal(5, GetLocation(build, "P:Contoso.Sources.Widget.Value")?.Line);
        Assert.Equal(13, GetLocation(build, "M:Contoso.Sources.Widget.ComputeAsync")?.Line);
        Assert.Null(GetLocation(build, "F:Contoso.Sources.Widget.Constant"));

        // Generated documents are not mapped by Source Link
        Assert.Null(GetLocation(build, "M:Contoso.Sources.Generated.Run"));
    }

    [Fact]
    public async Task EmbeddedPdb()
    {
        var assembly = Compile(embedded: true);
        var package = CreatePackage(p => p.AddFile("lib/net8.0/Contoso.Sources.dll", assembly.Assembly));
        await using var build = await BuildAsync(package);

        Assert.Equal(8, GetLocation(build, "M:Contoso.Sources.Widget.Compute(System.Int32)")?.Line);
    }

    [Fact]
    public async Task MissingPdb()
    {
        var assembly = Compile();
        var package = CreatePackage(p => p.AddFile("lib/net8.0/Contoso.Sources.dll", assembly.Assembly));
        await using var build = await BuildAsync(package);

        Assert.Null(GetLocation(build, "M:Contoso.Sources.Widget.Compute(System.Int32)"));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG3003" && d.Severity == Diagnostics.DiagnosticSeverity.Info);
    }

    [Fact]
    public async Task MismatchedPdb_IsNotUsed()
    {
        var assembly = Compile();
        var other = Compile(widgetSource: WidgetSource.Replace("return x * 2;", "return x * 3;", StringComparison.Ordinal));
        var package = CreatePackage(p => p.AddFile("lib/net8.0/Contoso.Sources.dll", assembly.Assembly).AddFile("lib/net8.0/Contoso.Sources.pdb", other.Pdb!));
        await using var build = await BuildAsync(package);

        Assert.Null(GetLocation(build, "M:Contoso.Sources.Widget.Compute(System.Int32)"));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG3005");
    }

    [Fact]
    public async Task ReferenceAssembly_IsMappedToTheImplementationByDocumentationId()
    {
        var implementation = Compile();
        var reference = Compile(referenceAssembly: true);
        var package = CreatePackage(p => p
            .AddFile("ref/net8.0/Contoso.Sources.dll", reference.Assembly)
            .AddFile("lib/net8.0/Contoso.Sources.dll", implementation.Assembly)
            .AddFile("lib/net8.0/Contoso.Sources.pdb", implementation.Pdb!));
        await using var build = await BuildAsync(package);

        Assert.True(build.Context.ApiAssemblies.Single().Asset.IsReferenceAssembly);
        Assert.Equal(8, GetLocation(build, "M:Contoso.Sources.Widget.Compute(System.Int32)")?.Line);
    }

    [Fact]
    public async Task AmbiguousRuntimeImplementations_HaveNoLink()
    {
        var windows = Compile();
        var linux = Compile(widgetSource: "\n\n" + WidgetSource);
        var reference = Compile(referenceAssembly: true);
        var package = CreatePackage(p => p
            .AddFile("ref/net8.0/Contoso.Sources.dll", reference.Assembly)
            .AddFile("runtimes/win/lib/net8.0/Contoso.Sources.dll", windows.Assembly)
            .AddFile("runtimes/win/lib/net8.0/Contoso.Sources.pdb", windows.Pdb!)
            .AddFile("runtimes/linux/lib/net8.0/Contoso.Sources.dll", linux.Assembly)
            .AddFile("runtimes/linux/lib/net8.0/Contoso.Sources.pdb", linux.Pdb!));
        await using var build = await BuildAsync(package);

        Assert.Null(GetLocation(build, "M:Contoso.Sources.Widget.Compute(System.Int32)"));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG3007");
    }

    [Fact]
    public async Task MultipleFrameworks_KeepTheirOwnLocations()
    {
        var net8 = Compile(widgetSource: WidgetSource.ReplaceLineEndings("\n").Replace("public class Widget\n    {", "public class Widget\n    {\n\n", StringComparison.Ordinal));
        var netstandard = Compile();
        var package = CreatePackage(p => p
            .AddFile("lib/net8.0/Contoso.Sources.dll", net8.Assembly)
            .AddFile("lib/net8.0/Contoso.Sources.pdb", net8.Pdb!)
            .AddFile("lib/netstandard2.0/Contoso.Sources.dll", netstandard.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Sources.pdb", netstandard.Pdb!));
        await using var build = await BuildAsync(package);

        var symbol = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("M:Contoso.Sources.Widget.Compute(System.Int32)"));
        var variants = build.Context.SourceLinks.GetLocations(symbol);
        Assert.Equal([(["net8.0"], 10), (["netstandard2.0"], 8)], variants.Select(v => (v.TargetFrameworks.ToArray(), v.Location.Line ?? 0)));
    }

    [Fact]
    public async Task UnsupportedHost()
    {
        var assembly = Compile(sourceLink: """{"documents":{"/_/*":"https://gitlab.example.com/contoso/library/-/raw/0123456789abcdef/*"}}""");
        var package = CreatePackage(p => p.AddFile("lib/net8.0/Contoso.Sources.dll", assembly.Assembly).AddFile("lib/net8.0/Contoso.Sources.pdb", assembly.Pdb!));
        await using var build = await BuildAsync(package);

        Assert.Null(GetLocation(build, "M:Contoso.Sources.Widget.Compute(System.Int32)"));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG3006" && d.Message.Contains("gitlab.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public void WildcardMapping_LongestPrefixWins()
    {
        var map = SourceLinkMap.TryParse("""{"documents":{"/_/*":"https://raw.githubusercontent.com/a/b/1/*","/_/external/*":"https://raw.githubusercontent.com/c/d/2/*","/exact.cs":"https://raw.githubusercontent.com/e/f/3/exact.cs"}}"""u8)!;

        Assert.Equal("https://raw.githubusercontent.com/a/b/1/src/A%20B.cs", map.GetUrl("/_/src/A B.cs"));
        Assert.Equal("https://raw.githubusercontent.com/c/d/2/x.cs", map.GetUrl("/_/external/x.cs"));
        Assert.Equal("https://raw.githubusercontent.com/e/f/3/exact.cs", map.GetUrl("/exact.cs"));
        Assert.Null(map.GetUrl("/obj/generated.cs"));
    }

    [Fact]
    public async Task SymbolServer()
    {
        var assembly = Compile();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var requests = new List<string>();
        app.Use(async (HttpContext context, RequestDelegate _) =>
        {
            requests.Add(context.Request.Path.Value!);
            if (context.Request.Path.Value!.EndsWith("/contoso.sources.pdb", StringComparison.Ordinal))
            {
                await context.Response.Body.WriteAsync(assembly.Pdb);
                return;
            }

            context.Response.StatusCode = 404;
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        var server = app.Urls.First();

        var package = CreatePackage(p => p.AddFile("lib/net8.0/Contoso.Sources.dll", assembly.Assembly));
        await using var build = await BuildAsync(package, $$""" "cacheDirectory": "cache", "sourceLinks": { "symbolServers": [ "{{server}}/symbols" ] }, """);

        Assert.Equal(8, GetLocation(build, "M:Contoso.Sources.Widget.Compute(System.Int32)")?.Line);
        var request = Assert.Single(requests);
        Assert.Matches("^/symbols/contoso.sources.pdb/[0-9a-f]{32}FFFFFFFF/contoso.sources.pdb$", request);
    }

    private static SourceLocation? GetLocation(TestBuild build, string documentationId)
    {
        var symbol = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId(documentationId));
        var locations = build.Context.SourceLinks.GetLocations(symbol);
        return locations.IsEmpty ? null : Assert.Single(locations).Location;
    }

    private static TestPackageBuilder CreatePackage(Action<TestPackageBuilder> configure)
    {
        var builder = new TestPackageBuilder("Contoso.Sources", "1.0.0").AddFile("README.md", "x");
        configure(builder);
        return builder;
    }

    private static async Task<TestBuild> BuildAsync(TestPackageBuilder package, string extra = "")
    {
        var build = TestBuild.Create($$"""{ "version": 1, {{extra}} "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new SourceLinkStage());
        return build;
    }
}
