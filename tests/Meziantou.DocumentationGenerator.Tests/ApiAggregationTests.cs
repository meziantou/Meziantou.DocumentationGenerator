using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class ApiAggregationTests
{
    private const string VariantsSource = """
        using System;

        namespace Contoso.Variants
        {
            /// <summary>A widget.</summary>
            public class Widget
        #if NET8
                : IDisposable
        #endif
            {
        #if NET8
                /// <summary>The name (net8.0).</summary>
                public string? Name { get; set; }

                /// <summary>Disposes.</summary>
                public void Dispose() { }

                /// <summary>Creates.</summary>
                public T Create<T>() where T : class, new() => new T();

                /// <summary>Old.</summary>
                [Obsolete("Old")]
                public void Old() { }
        #else
                /// <summary>The name (netstandard2.0).</summary>
                public string Name { get; set; } = "";

                /// <summary>Creates.</summary>
                public T Create<T>() where T : new() => new T();

                /// <summary>Old.</summary>
                public void Old() { }
        #endif

                /// <summary>Common.</summary>
                public void Common() { }
            }

        #if NET8
            /// <summary>Only in net8.0.</summary>
            public class OnlyNet8 { }
        #endif
        }
        """;

    private static readonly Lazy<CompiledAssembly> Variants = new(() => TestCompiler.Compile("Contoso.Variants", VariantsSource));
    private static readonly Lazy<CompiledAssembly> VariantsNet8 = new(() => TestCompiler.Compile("Contoso.Variants", VariantsSource, new CompileOptions { Defines = ["NET8"] }));

    private static TestPackageBuilder CreatePackage(string id = "Contoso.Variants", bool includeNet8 = true)
    {
        var builder = new TestPackageBuilder(id, "1.0.0")
            .AddFile("README.md", "# Variants")
            .AddFile("lib/netstandard2.0/Contoso.Variants.dll", Variants.Value.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Variants.xml", Variants.Value.Xml);
        if (includeNet8)
        {
            builder.AddFile("lib/net8.0/Contoso.Variants.dll", VariantsNet8.Value.Assembly).AddFile("lib/net8.0/Contoso.Variants.xml", VariantsNet8.Value.Xml);
        }

        return builder;
    }

    [Fact]
    public async Task TypeAvailableInOneFrameworkOnly()
    {
        var catalog = await AggregateAsync(CreatePackage());
        var type = Find<ApiTypeModel>(catalog, "T:Contoso.Variants.OnlyNet8");

        Assert.Equal(["net8.0"], type.Availability.TargetFrameworks);
        Assert.False(type.Availability.IsAvailableInAllTargetFrameworks);
        Assert.Equal(["net8.0", "netstandard2.0"], type.Assembly.TargetFrameworks);
    }

    [Fact]
    public async Task CommonMember_HasASingleVariant()
    {
        var catalog = await AggregateAsync(CreatePackage());
        var member = Find<ApiMemberModel>(catalog, "M:Contoso.Variants.Widget.Common");

        Assert.True(member.Availability.IsAvailableInAllTargetFrameworks);
        var variant = Assert.Single(member.Variants);
        Assert.Equal(["net8.0", "netstandard2.0"], variant.TargetFrameworks);
        Assert.Equal(PublicApiSymbolDifferences.None, member.Differences);
    }

    [Fact]
    public async Task FrameworkSpecificOverload()
    {
        var catalog = await AggregateAsync(ApiModelTests.CreateKitchenSinkPackage());
        var span = Find<ApiMemberModel>(catalog, "M:Contoso.Library.Client.Send(System.ReadOnlySpan{System.Byte})");
        var text = Find<ApiMemberModel>(catalog, "M:Contoso.Library.Client.Send(System.String)");

        Assert.Equal(["net8.0"], span.Availability.TargetFrameworks);
        Assert.True(text.Availability.IsAvailableInAllTargetFrameworks);
        Assert.Same(span.DeclaringType, text.DeclaringType);
        Assert.Equal(3, span.DeclaringType.Members.Count(m => m.OverloadName == "Send"));
    }

    [Theory]
    [InlineData("P:Contoso.Variants.Widget.Name", PublicApiSymbolDifferences.Nullability)]
    [InlineData("M:Contoso.Variants.Widget.Create``1", PublicApiSymbolDifferences.Constraints)]
    [InlineData("M:Contoso.Variants.Widget.Old", PublicApiSymbolDifferences.Attributes)]
    [InlineData("T:Contoso.Variants.Widget", PublicApiSymbolDifferences.Inheritance)]
    public async Task DifferingDeclarations_ArePreservedAsVariants(string documentationId, PublicApiSymbolDifferences expectedDifference)
    {
        var catalog = await AggregateAsync(CreatePackage());
        var symbol = Find<ApiSymbolModel>(catalog, documentationId);

        Assert.True(symbol.Differences.HasFlag(expectedDifference), $"Actual differences: {symbol.Differences}");
        Assert.HasCount(2, symbol.Variants);
        Assert.Equal([["net8.0"], ["netstandard2.0"]], symbol.Variants.Select(v => v.TargetFrameworks.ToArray()));
        Assert.NotEqual(DeclarationFormatter.Format(symbol.Variants[0].Symbol).Text, DeclarationFormatter.Format(symbol.Variants[1].Symbol).Text);
    }

    [Fact]
    public async Task Variants_KeepTheirProvenance()
    {
        var catalog = await AggregateAsync(CreatePackage());
        var name = Find<ApiMemberModel>(catalog, "P:Contoso.Variants.Widget.Name");

        foreach (var variant in name.Variants)
        {
            var targetFramework = Assert.Single(variant.TargetFrameworks);
            var model = name.Assembly.GetModel(targetFramework);
            Assert.Equal(model.Assembly.Module.ModuleVersionId, variant.Symbol.Origin!.ModuleVersionId);
            Assert.Equal($"lib/{targetFramework}/Contoso.Variants.xml", model.Asset.XmlDocumentationEntry);
        }
    }

    [Fact]
    public async Task Aggregation_IsIndependentOfInputOrder()
    {
        var package = CreatePackage();
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());
        var models = build.Context.ApiAssemblies;
        var packages = build.Context.DiscoveredPackages;

        var forward = Describe(new ApiAggregator(ApiSymbolPolicy.Default, new DiagnosticBag()).Aggregate(models, packages));
        var reversed = Describe(new ApiAggregator(ApiSymbolPolicy.Default, new DiagnosticBag()).Aggregate(models.Reverse(), packages));
        Assert.Equal(forward, reversed);
    }

    [Fact]
    public async Task AddingAFramework_DoesNotDuplicateSymbols()
    {
        var single = await AggregateAsync(CreatePackage(includeNet8: false));
        var multiple = await AggregateAsync(CreatePackage());

        var singleIds = single.GetAllTypes().Select(t => t.Identity).ToList();
        var multipleIds = multiple.GetAllTypes().Select(t => t.Identity).ToList();
        Assert.Distinct(multipleIds);
        Assert.Subset(multipleIds.ToHashSet(), singleIds.ToHashSet());
        Assert.Single(multiple.FindByDocumentationId("M:Contoso.Variants.Widget.Common"));
    }

    [Fact]
    public async Task SameNamesInDifferentPackages_StayDistinct()
    {
        var first = CreatePackage("Contoso.First");
        var second = CreatePackage("Contoso.Second");
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(first)}}, {{TestBuild.PackageReference(second)}} ] }""", [first, second]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage());
        var catalog = build.Context.ApiCatalog;

        Assert.HasCount(2, catalog.Assemblies);
        var widgets = catalog.FindByDocumentationId("T:Contoso.Variants.Widget");
        Assert.Equal(["Contoso.First/Contoso.Variants", "Contoso.Second/Contoso.Variants"], widgets.Select(w => w.Identity.Scope));
        Assert.Equal(["Contoso.First", "Contoso.Second"], catalog.Packages.Select(p => p.Id));
        Assert.All(catalog.Packages, p => Assert.Single(p.Assemblies));
    }

    [Fact]
    public async Task DuplicateInputs_AreReported()
    {
        var package = CreatePackage();
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());
        var diagnostics = new DiagnosticBag();
        var model = build.Context.ApiAssemblies[0];

        var catalog = new ApiAggregator(ApiSymbolPolicy.Default, diagnostics).Aggregate([model, model], build.Context.DiscoveredPackages);
        Assert.Empty(catalog.Assemblies);
        Assert.Equal("DG2013", Assert.Single(diagnostics.GetDiagnostics()).Code);
    }

    [Fact]
    public async Task NamespacesAndNestedTypes()
    {
        var catalog = await AggregateAsync(ApiModelTests.CreateKitchenSinkPackage());
        var package = Assert.Single(catalog.Packages);

        Assert.Equal(["Contoso.Library", "Contoso.Library.Collections"], package.Namespaces.Select(n => n.Name));
        var collections = package.Namespaces[1];
        Assert.Equal(["Tree`1", "Tree`1.Node", "Tree`1.Node.Child`1"], collections.Types.Select(t => t.MetadataName));
        Assert.Equal(["Tree<T>", "Tree<T>.Node", "Tree<T>.Node.Child<TKey>"], collections.Types.Select(t => t.DisplayName));
        var node = collections.Types[1];
        Assert.Equal("Tree`1", node.DeclaringType?.MetadataName);
        Assert.Contains(node.NestedTypes, t => t.MetadataName == "Tree`1.Node.Child`1");
    }

    [Fact]
    public async Task MemberCategories_AreOrdered()
    {
        var catalog = await AggregateAsync(ApiModelTests.CreateKitchenSinkPackage());
        var client = Find<ApiTypeModel>(catalog, "T:Contoso.Library.Client");

        Assert.Equal(
            [ApiMemberCategory.Constructor, ApiMemberCategory.Property, ApiMemberCategory.Indexer, ApiMemberCategory.Method, ApiMemberCategory.Operator, ApiMemberCategory.Event, ApiMemberCategory.Field, ApiMemberCategory.Constant],
            client.Members.Select(m => m.Category).Distinct());
        Assert.Equal(ApiMemberCategory.EnumValue, Find<ApiMemberModel>(catalog, "F:Contoso.Library.MessageKind.Text").Category);
    }

    private static string Describe(ApiCatalog catalog)
    {
        return string.Join('\n', catalog.GetAllTypes().SelectMany(t => new ApiSymbolModel[] { t }.Concat(t.Members))
            .Select(s => s.DocumentationId + " " + string.Join(';', s.Variants.Select(v => string.Join(',', v.TargetFrameworks)))));
    }

    private static T Find<T>(ApiCatalog catalog, string documentationId)
        where T : ApiSymbolModel
    {
        return Assert.IsAssignableTo<T>(Assert.Single(catalog.FindByDocumentationId(documentationId)));
    }

    private static async Task<ApiCatalog> AggregateAsync(TestPackageBuilder package)
    {
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage());
        return build.Context.ApiCatalog;
    }
}
