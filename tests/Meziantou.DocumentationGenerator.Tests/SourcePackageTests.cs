using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using NuGet.Frameworks;
using Meziantou.Framework.PublicApiGenerator;
using Element = Meziantou.Framework.WebPlatform.Dom.Element;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class SourcePackageTests
{
    private const string GuardSource = """
        #nullable enable
        namespace Contoso.Sources;

        /// <summary>Argument validation helpers.</summary>
        internal static class Guard
        {
            /// <summary>Throws when <paramref name="value"/> is <see langword="null"/>.</summary>
            /// <param name="value">The value.</param>
            public static T NotNull<T>(T? value) where T : class => value ?? throw new System.ArgumentNullException(nameof(value));

        #if NET8_0_OR_GREATER
            /// <summary>Throws when the span is empty.</summary>
            internal static void NotEmpty(System.ReadOnlySpan<char> value) { }
        #endif

            private static void Hidden() { }
        }

        /// <summary>Options of <see cref="Guard"/>.</summary>
        sealed class GuardOptions
        {
            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = "";

            /// <summary>Gets the label, set by the consuming project only.</summary>
            public string Label { get; internal set; } = "";

            /// <summary>Not usable outside of the type hierarchy of the consuming project.</summary>
            private protected void Reset() { }

            /// <summary>The default options.</summary>
            internal static readonly GuardOptions Default = new(), Empty = new();
        }

        /// <summary>A type declared public in one part.</summary>
        public partial class PublicPart { }
        """;

    private const string PartialSource = """
        namespace Contoso.Sources;

        partial class PublicPart { }
        """;

    private static TestPackageBuilder CreateSourcePackage(string id = "Contoso.Sources") => new TestPackageBuilder(id, "1.0.0")
        .AddFile("lib/netstandard2.0/_._", "")
        .AddFile("contentFiles/cs/netstandard2.0/Guard.cs", GuardSource)
        .AddFile("contentFiles/cs/netstandard2.0/Internal/PublicPart.cs", PartialSource)
        .AddFile("contentFiles/cs/net8.0/Guard.cs", GuardSource)
        .AddFile("contentFiles/cs/net8.0/Internal/PublicPart.cs", PartialSource)
        .AddFile("contentFiles/cs/net8.0/Template.cs.pp", "namespace $rootnamespace$; class Template { }");

    [Fact]
    public async Task SourcePackage_CompiledPerFrameworkWithInternalSymbols()
    {
        var package = CreateSourcePackage();
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());

        Assert.DoesNotContain(build.Diagnostics, d => d.Code is "DG2007" or "DG2014");
        var discovered = Assert.Single(build.Context.DiscoveredPackages);
        Assert.True(discovered.IsSourcePackage);
        Assert.Equal(["net8.0", "netstandard2.0"], discovered.Assets.Select(a => a.TargetFramework));
        Assert.All(discovered.Assets, a => Assert.Equal("Contoso.Sources", a.AssemblyName));

        var net8 = discovered.Assets.Single(a => a.TargetFramework == "net8.0");
        Assert.Equal("contentFiles/cs/net8.0/", net8.AssemblyEntry);
        Assert.Equal(["contentFiles/cs/net8.0/Guard.cs", "contentFiles/cs/net8.0/Internal/PublicPart.cs"], net8.CompiledSource!.SourceEntries);

        // Internal symbols are read with their declared accessibility
        var net8Model = build.Context.ApiAssemblies.Single(a => a.TargetFramework == "net8.0").Assembly;
        var netstandardModel = build.Context.ApiAssemblies.Single(a => a.TargetFramework == "netstandard2.0").Assembly;
        Assert.Equal(PublicApiAccessibility.Internal, net8Model.FindSymbolByDocumentationId("T:Contoso.Sources.Guard")!.Accessibility);
        Assert.Equal(PublicApiAccessibility.Internal, net8Model.FindSymbolByDocumentationId("T:Contoso.Sources.GuardOptions")!.Accessibility);
        Assert.Equal(PublicApiAccessibility.Internal, net8Model.FindSymbolByDocumentationId("F:Contoso.Sources.GuardOptions.Empty")!.Accessibility);
        Assert.Equal(PublicApiAccessibility.Public, net8Model.FindSymbolByDocumentationId("T:Contoso.Sources.PublicPart")!.Accessibility);

        // #if branches follow the target framework, and private members are not part of the API
        Assert.NotNull(net8Model.FindSymbolByDocumentationId("M:Contoso.Sources.Guard.NotEmpty(System.ReadOnlySpan{System.Char})"));
        Assert.Null(netstandardModel.FindSymbolByDocumentationId("M:Contoso.Sources.Guard.NotEmpty(System.ReadOnlySpan{System.Char})"));
        Assert.Null(net8Model.FindSymbolByDocumentationId("M:Contoso.Sources.Guard.Hidden"));
        Assert.Null(net8Model.FindSymbolByDocumentationId("M:Contoso.Sources.GuardOptions.Reset"));
        Assert.Null(net8Model.FindType("Contoso.Template"));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG2011" && d.Message.Contains("source package compiled in memory", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SourcePackage_ReferencesConfiguredPackages()
    {
        var library = TestCompiler.Compile("Contoso.Library", "namespace Contoso.Library; /// <summary>A client.</summary>\npublic class Client { }");
        var libraryPackage = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .AddFile("lib/net8.0/Contoso.Library.dll", library.Assembly)
            .AddFile("lib/net8.0/Contoso.Library.xml", library.Xml);
        var sources = new TestPackageBuilder("Contoso.Sources", "1.0.0")
            .AddFile("contentFiles/any/net8.0/ClientExtensions.cs", """
                namespace Contoso.Sources;

                /// <summary>Extensions of <see cref="Contoso.Library.Client"/>.</summary>
                internal static class ClientExtensions
                {
                    /// <summary>Clones the client.</summary>
                    public static Contoso.Library.Client Clone(this Contoso.Library.Client client) => client;
                }
                """);
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(sources)}} ], "references": [ {{TestBuild.PackageReference(libraryPackage)}} ] }""", [sources, libraryPackage]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());

        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG2014");
        var asset = Assert.Single(build.Context.DiscoveredPackages.Single().Assets);
        Assert.Equal("contentFiles/any/net8.0/", asset.AssemblyEntry);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG2011" && d.Message.Contains("Contoso.Library@1.0.0 (net8.0)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SourcePackage_AnyFramework_ReferencesAssetsForTheRunningRuntime()
    {
        // Like Meziantou.Framework.Roslyn: contentFiles/cs/any/ sources that use a package the nuspec doesn't declare
        var library = TestCompiler.Compile("Contoso.Library", "namespace Contoso.Library; public class Client { }");
        var libraryPackage = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .AddFile("lib/netstandard2.0/Contoso.Library.dll", library.Assembly)
            .AddFile("lib/net8.0/Contoso.Library.dll", library.Assembly);
        var sources = new TestPackageBuilder("Contoso.Sources", "1.0.0")
            .AddFile("contentFiles/cs/any/Contoso.Sources/ClientExtensions.cs", "namespace Contoso.Sources; internal static class ClientExtensions { public static Contoso.Library.Client Clone(this Contoso.Library.Client client) => client; }");
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(sources)}} ], "references": [ {{TestBuild.PackageReference(libraryPackage)}} ] }""", [sources, libraryPackage]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());

        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG2014");
        var asset = Assert.Single(build.Context.DiscoveredPackages.Single().Assets);
        Assert.Equal("any", asset.TargetFramework);
        Assert.Equal("contentFiles/cs/any/", asset.AssemblyEntry);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG2011" && d.Message.Contains("Contoso.Library@1.0.0 (net8.0)", StringComparison.Ordinal));
        Assert.NotNull(build.Context.ApiAssemblies.Single(a => a.Package.IsSourcePackage).Assembly.FindType("Contoso.Sources.ClientExtensions"));
    }

    [Fact]
    public async Task SourcePackage_DefineConstants()
    {
        var sources = new TestPackageBuilder("Contoso.Sources", "1.0.0")
            .AddFile("contentFiles/cs/net8.0/Feature.cs", """
                namespace Contoso.Sources;

                internal static class Feature
                {
                #if CONTOSO_FEATURE && NET8_0_OR_GREATER
                    public static void Enabled() { }
                #endif
                #if CONTOSO_OTHER
                    public static void Other() { }
                #endif
                }
                """);
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(sources, "\"defineConstants\": [ \"CONTOSO_FEATURE\", \"CONTOSO_FEATURE\" ]")}} ] }""", [sources]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());

        Assert.Equal(["CONTOSO_FEATURE"], build.Context.Configuration.Packages[0].DefineConstants);
        var model = build.Context.ApiAssemblies.Single().Assembly;
        Assert.NotNull(model.FindSymbolByDocumentationId("M:Contoso.Sources.Feature.Enabled"));
        Assert.Null(model.FindSymbolByDocumentationId("M:Contoso.Sources.Feature.Other"));
    }

    [Theory]
    [InlineData("1ABC")]
    [InlineData("A-B")]
    [InlineData("")]
    public void DefineConstants_InvalidSymbolsAreRejected(string symbol)
    {
        var package = new TestPackageBuilder("Contoso.Sources", "1.0.0").AddFile("contentFiles/cs/net8.0/A.cs", "class A { }");
        var exception = Assert.Throws<InvalidOperationException>(() => TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package, "\"defineConstants\": [ \"" + symbol + "\" ]")}} ] }""", [package]));
        Assert.Contains("DG1013", exception.Message, StringComparison.Ordinal);
        Assert.Contains("defineConstants[0]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourcePackage_CompilationErrorsAreReported()
    {
        var package = new TestPackageBuilder("Contoso.Sources", "1.0.0")
            .AddFile("contentFiles/cs/net8.0/Broken.cs", "namespace Contoso.Sources; internal static class Broken { public static Contoso.Missing.Thing Create() => null; }");
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage());

        var diagnostic = Assert.Single(build.Diagnostics, d => d.Code == "DG2014");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("contentFiles/cs/net8.0/Broken.cs(1,", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("CS0234", diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(build.Context.DiscoveredPackages.Single().Assets);
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG2007");
    }

    [Fact]
    public async Task SourcePackage_MethodBodyErrorsDoNotPreventDocumentation()
    {
        var package = new TestPackageBuilder("Contoso.Sources", "1.0.0")
            .AddFile("contentFiles/cs/net8.0/Helper.cs", "namespace Contoso.Sources; internal static class Helper { public static int Get() => Contoso.Missing.Value; }");
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage());

        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG2014");
        Assert.Single(build.Context.DiscoveredPackages.Single().Assets);
    }

    [Fact]
    public async Task SourcePackage_TypePageShowsInternalDeclarations()
    {
        await using var site = await SiteBuilder.BuildAsync([CreateSourcePackage()]);
        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);

        var guard = HtmlDocumentLoader.Parse(site.ReadFile("api/Contoso.Sources.Guard.html"));
        Assert.Equal("Guard Class", guard.QuerySelector("h1")!.TextContent);
        Assert.Contains("Compiled into the consuming project (source package)", guard.QuerySelector("dl.api-metadata")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("internal static class Guard", guard.QuerySelector("pre.declaration")!.TextContent);
        Assert.Contains("Argument validation helpers.", guard.QuerySelector("article")!.TextContent, StringComparison.Ordinal);

        var declarations = guard.QuerySelectorAll<Element>("section.api-member pre.declaration").Select(d => d.TextContent ?? "").ToList();
        Assert.Contains(declarations, d => d.StartsWith("public static T NotNull<T>(", StringComparison.Ordinal));
        Assert.Contains("internal static void NotEmpty(ReadOnlySpan<char> value)", declarations);

        var options = HtmlDocumentLoader.Parse(site.ReadFile("api/Contoso.Sources.GuardOptions.html"));
        Assert.Equal("internal sealed class GuardOptions", options.QuerySelector("pre.declaration")!.TextContent);
        Assert.Equal("public string Label { get; internal set; }", options.GetElementById("Label")!.QuerySelector("pre.declaration")!.TextContent);
        Assert.Equal("internal static readonly GuardOptions Default", options.GetElementById("Default")!.QuerySelector("pre.declaration")!.TextContent);
        Assert.Null(options.GetElementById("Reset"));

        var publicPart = HtmlDocumentLoader.Parse(site.ReadFile("api/Contoso.Sources.PublicPart.html"));
        Assert.StartsWith("public class PublicPart", publicPart.QuerySelector("pre.declaration")!.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("net8.0", new[] { "NET", "NET8_0", "NET5_0_OR_GREATER", "NET8_0_OR_GREATER", "NETCOREAPP", "NETCOREAPP3_1_OR_GREATER" }, new[] { "NET9_0_OR_GREATER", "NETSTANDARD" })]
    [InlineData("netstandard2.0", new[] { "NETSTANDARD", "NETSTANDARD2_0", "NETSTANDARD1_6_OR_GREATER", "NETSTANDARD2_0_OR_GREATER" }, new[] { "NETSTANDARD2_1_OR_GREATER", "NET" })]
    [InlineData("net472", new[] { "NETFRAMEWORK", "NET472", "NET46_OR_GREATER", "NET472_OR_GREATER" }, new[] { "NET48_OR_GREATER", "NETCOREAPP" })]
    [InlineData("netcoreapp3.1", new[] { "NETCOREAPP", "NETCOREAPP3_1", "NETCOREAPP2_1_OR_GREATER" }, new[] { "NET", "NET5_0_OR_GREATER" })]
    [InlineData("net8.0-windows10.0.19041", new[] { "NET8_0", "WINDOWS", "WINDOWS_OR_GREATER" }, new[] { "NETFRAMEWORK" })]
    public void PreprocessorSymbols(string framework, string[] expected, string[] unexpected)
    {
        var symbols = FrameworkPreprocessorSymbols.Get(NuGetFramework.ParseFolder(framework));
        Assert.All(expected, s => Assert.Contains(s, symbols));
        Assert.All(unexpected, s => Assert.DoesNotContain(s, symbols));
    }
}
