using System.Text;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class DiscoveryTests
{
    private static readonly Lazy<CompiledAssembly> Library = new(() => TestCompiler.Compile("Contoso.Library", """
        namespace Contoso;

        /// <summary>A client.</summary>
        public class Client
        {
            /// <summary>Sends.</summary>
            public void Send() { }
        }
        """));

    private static readonly Lazy<CompiledAssembly> LibraryReference = new(() => TestCompiler.Compile("Contoso.Library", """
        namespace Contoso;

        /// <summary>A client.</summary>
        public class Client
        {
            /// <summary>Sends.</summary>
            public void Send() { }
        }
        """, new CompileOptions { ReferenceAssembly = true }));

    private static readonly Lazy<CompiledAssembly> Extensions = new(() => TestCompiler.Compile("Contoso.Extensions", "namespace Contoso.Extensions; public static class Helpers { }"));

    [Fact]
    public async Task RefAndLib_UsesReferenceAssemblyForApis()
    {
        await using var temp = TemporaryDirectory.Create();
        var package = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .WithNuspecMetadata("<readme>README.md</readme>")
            .AddFile("README.md", "# Readme")
            .AddFile("ref/net8.0/Contoso.Library.dll", LibraryReference.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Library.dll", Library.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Library.pdb", Library.Value.Pdb!)
            .AddFile("lib/net8.0/Contoso.Library.xml", Library.Value.Xml)
            .AddFile("runtimes/win/lib/net8.0/Contoso.Library.dll", Library.Value.Assembly);

        var (discovered, diagnostics) = await DiscoverAsync(temp, package);

        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        var asset = Assert.Single(discovered.Assets);
        Assert.Equal("ref/net8.0/Contoso.Library.dll", asset.AssemblyEntry);
        Assert.Equal("net8.0", asset.TargetFramework);
        Assert.Equal("Contoso.Library", asset.AssemblyName);
        Assert.True(asset.IsReferenceAssembly);
        Assert.Equal("lib/net8.0/Contoso.Library.xml", asset.XmlDocumentationEntry);
        Assert.Equal(
            [new SymbolCandidate("lib/net8.0/Contoso.Library.dll", "lib/net8.0/Contoso.Library.pdb", null), new SymbolCandidate("runtimes/win/lib/net8.0/Contoso.Library.dll", null, "win")],
            asset.SymbolCandidates);
        Assert.Contains(diagnostics, d => d.Code == "DG2011" && d.Message.Contains("reference assembly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibOnly_SeveralFrameworksAndAssemblies()
    {
        await using var temp = TemporaryDirectory.Create();
        var package = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .AddFile("lib/net8.0/Contoso.Library.dll", Library.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Library.xml", Library.Value.Xml)
            .AddFile("lib/net8.0/Contoso.Extensions.dll", Extensions.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Extensions.xml", Extensions.Value.Xml)
            .AddFile("lib/net8.0/fr/Contoso.Library.resources.dll", "satellite")
            .AddFile("lib/netstandard2.0/Contoso.Library.dll", Library.Value.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Library.xml", Library.Value.Xml);

        var (discovered, _) = await DiscoverAsync(temp, package);

        Assert.Equal(
            ["Contoso.Extensions net8.0", "Contoso.Library net8.0", "Contoso.Library netstandard2.0"],
            discovered.Assets.Select(a => a.AssemblyName + " " + a.TargetFramework));
        Assert.All(discovered.Assets, a => Assert.False(a.IsReferenceAssembly));
        Assert.All(discovered.Assets, a => Assert.Equal(a.AssemblyEntry, Assert.Single(a.SymbolCandidates).AssemblyEntry));
    }

    [Fact]
    public async Task MissingXml_IsReported()
    {
        await using var temp = TemporaryDirectory.Create();
        var package = new TestPackageBuilder("Contoso.Library", "1.0.0").AddFile("lib/net8.0/Contoso.Library.dll", Library.Value.Assembly);

        var (discovered, diagnostics) = await DiscoverAsync(temp, package);

        Assert.Null(Assert.Single(discovered.Assets).XmlDocumentationEntry);
        Assert.Contains(diagnostics, d => d.Code == "DG3002" && d.Severity == DiagnosticSeverity.Warning && d.Location?.TargetFramework == "net8.0");
    }

    [Fact]
    public async Task XmlDocumentation_IsNotBorrowedFromAnotherFramework()
    {
        await using var temp = TemporaryDirectory.Create();
        var package = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .AddFile("README.md", "# Readme")
            .AddFile("lib/net8.0/Contoso.Library.dll", Library.Value.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Library.dll", Library.Value.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Library.xml", Library.Value.Xml);

        var (discovered, _) = await DiscoverAsync(temp, package);

        Assert.Null(discovered.Assets.Single(a => a.TargetFramework == "net8.0").XmlDocumentationEntry);
        Assert.NotNull(discovered.Assets.Single(a => a.TargetFramework == "netstandard2.0").XmlDocumentationEntry);
    }

    [Fact]
    public async Task NativeOnlyPackage_HasNoAssets()
    {
        await using var temp = TemporaryDirectory.Create();
        var package = new TestPackageBuilder("Native", "1.0.0").AddFile("runtimes/linux-x64/native/libnative.so", "elf");

        var (discovered, diagnostics) = await DiscoverAsync(temp, package);

        Assert.Empty(discovered.Assets);
        Assert.Contains(diagnostics, d => d.Code == "DG2007" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task Placeholders()
    {
        await using var temp = TemporaryDirectory.Create();
        var package = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .AddFile("lib/net8.0/_._", "")
            .AddFile("lib/netstandard2.0/Contoso.Library.dll", Library.Value.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Library.xml", Library.Value.Xml);

        var (discovered, _) = await DiscoverAsync(temp, package);

        Assert.Equal(["net8.0"], discovered.PlaceholderFrameworks.Select(f => f.GetShortFolderName()));
        Assert.Equal("netstandard2.0", Assert.Single(discovered.Assets).TargetFramework);
    }

    [Fact]
    public async Task InvalidManagedAssembly_IsIgnored()
    {
        await using var temp = TemporaryDirectory.Create();
        var package = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .AddFile("lib/net8.0/Native.dll", "MZ not really a PE file")
            .AddFile("lib/net8.0/Contoso.Library.dll", Library.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Library.xml", Library.Value.Xml)
            .AddFile("lib/unknownframework1.0/Contoso.Library.dll", Library.Value.Assembly);

        var (discovered, diagnostics) = await DiscoverAsync(temp, package);

        Assert.Equal("Contoso.Library", Assert.Single(discovered.Assets).AssemblyName);
        Assert.Contains(diagnostics, d => d.Code == "DG2008" && d.Message.Contains("Native.dll", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG2008" && d.Message.Contains("unknownframework1.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Filters()
    {
        await using var temp = TemporaryDirectory.Create();
        var package = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .AddFile("lib/net8.0/Contoso.Library.dll", Library.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Extensions.dll", Extensions.Value.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Library.dll", Library.Value.Assembly);

        var (discovered, diagnostics) = await DiscoverAsync(temp, package, """ "targetFrameworks": [ "net8.0" ], "assemblies": [ "Contoso.Library.dll" ] """);
        var asset = Assert.Single(discovered.Assets);
        Assert.Equal("Contoso.Library net8.0", asset.AssemblyName + " " + asset.TargetFramework);
        Assert.DoesNotContain(diagnostics, d => d.Code == "DG2004");

        (_, diagnostics) = await DiscoverAsync(temp, package, """ "targetFrameworks": [ "net6.0" ], "assemblies": [ "Missing.dll" ] """);
        Assert.Contains(diagnostics, d => d.Code == "DG2004" && d.Severity == DiagnosticSeverity.Error && d.Message.Contains("net6.0", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG2004" && d.Message.Contains("Missing.dll", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnconfiguredDependencies_AreReported()
    {
        await using var temp = TemporaryDirectory.Create();
        var package = new TestPackageBuilder("Contoso.Library", "1.0.0")
            .WithNuspecMetadata("""<dependencies><group targetFramework="net8.0"><dependency id="Contoso.Core" version="[1.0.0, )" /><dependency id="Contoso.Configured" version="2.0.0" /></group></dependencies>""")
            .AddFile("README.md", "# Readme")
            .AddFile("lib/net8.0/Contoso.Library.dll", Library.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Library.xml", Library.Value.Xml);
        var reference = new TestPackageBuilder("Contoso.Configured", "2.0.0");
        package.Save(temp.FullPath / "packages");
        reference.Save(temp.FullPath / "packages");

        var diagnostics = new DiagnosticBag();
        var configuration = ConfigurationLoader.Load(Encoding.UTF8.GetBytes("""
            {
              "version": 1,
              "packages": [ { "id": "Contoso.Library", "version": "1.0.0", "path": "packages/contoso.library.1.0.0.nupkg" } ],
              "references": [ { "id": "Contoso.Configured", "version": "2.0.0", "path": "packages/contoso.configured.2.0.0.nupkg" } ]
            }
            """), temp.FullPath / "docgen.json", diagnostics)!;
        var context = new BuildContext(configuration, new BuildOptions { ConfigurationFile = configuration.ConfigurationFile }, diagnostics, NullBuildReporter.Instance);
        var status = await BuildCoordinator.RunStagesAsync(context, [new AcquisitionStage(), new DiscoveryStage()], TestContext.Current.CancellationToken);

        Assert.Equal(BuildStatus.Succeeded, status);
        Assert.Single(context.DiscoveredPackages);
        Assert.Single(context.DiscoveredReferences);
        Assert.DoesNotContain("Contoso.Configured", context.Configuration.Products.SelectMany(p => p.Packages));
        var diagnostic = Assert.Single(diagnostics.GetDiagnostics(), d => d.Code == "DG2010");
        Assert.Contains("'Contoso.Core' ([1.0.0, ))", diagnostic.Message);
    }

    private static Task<(DiscoveredPackage Package, IReadOnlyList<Diagnostic> Diagnostics)> DiscoverAsync(TemporaryDirectory directory, TestPackageBuilder builder, string? packageOptions = null)
    {
        var path = builder.Save(directory.FullPath / "packages");
        var diagnostics = new DiagnosticBag();
        var options = packageOptions is null ? "" : "," + packageOptions;
        var configuration = ConfigurationLoader.Load(Encoding.UTF8.GetBytes($$"""{ "version": 1, "packages": [ { "id": "{{builder.Id}}", "version": "{{builder.Version}}", "path": "packages/{{path.Name}}" {{options}} } ] }"""), directory.FullPath / "docgen.json", diagnostics);
        Assert.NotNull(configuration);
        using var reader = new NuGet.Packaging.PackageArchiveReader(path);
        var package = new AcquiredPackage
        {
            Configuration = configuration.Packages[0],
            Path = path,
            Metadata = PackageMetadata.Read(reader.NuspecReader),
            Sha512 = "",
            Source = "local",
        };

        var discovered = new PackageDiscoverer(diagnostics).Discover(package);
        return Task.FromResult((Assert.NotNull(discovered), diagnostics.GetDiagnostics()));
    }
}
