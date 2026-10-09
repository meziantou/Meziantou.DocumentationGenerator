using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class ApiModelTests
{
    internal static readonly Lazy<CompiledAssembly> KitchenSink = new(() => TestCompiler.Compile("Contoso.Library", TestFixtures.KitchenSink));
    internal static readonly Lazy<CompiledAssembly> KitchenSinkNet8 = new(() => TestCompiler.Compile("Contoso.Library", TestFixtures.KitchenSink, new CompileOptions { Defines = ["NET8"] }));

    internal static TestPackageBuilder CreateKitchenSinkPackage(string id = "Contoso.Library") => new TestPackageBuilder(id, "1.0.0")
        .WithNuspecMetadata("<readme>README.md</readme>")
        .AddFile("README.md", "# Contoso.Library")
        .AddFile("lib/netstandard2.0/Contoso.Library.dll", KitchenSink.Value.Assembly)
        .AddFile("lib/netstandard2.0/Contoso.Library.xml", KitchenSink.Value.Xml)
        .AddFile("lib/net8.0/Contoso.Library.dll", KitchenSinkNet8.Value.Assembly)
        .AddFile("lib/net8.0/Contoso.Library.xml", KitchenSinkNet8.Value.Xml);

    [Fact]
    public async Task ReadsEveryCategory_WithCompilerDocumentationIds()
    {
        var model = await ReadAsync();
        var documented = model.Assembly.GetAllSymbols().Where(ApiSymbolPolicy.Default.IsDocumented).Select(s => s.DocumentationId).ToHashSet(StringComparer.Ordinal);

        string[] expected =
        [
            "T:Contoso.Library.Client",
            "M:Contoso.Library.Client.#ctor",
            "M:Contoso.Library.Client.#ctor(System.String)",
            "F:Contoso.Library.Client.DefaultPort",
            "F:Contoso.Library.Client.Shared",
            "P:Contoso.Library.Client.Name",
            "P:Contoso.Library.Client.Item(System.Int32)",
            "P:Contoso.Library.Client.Item(System.String)",
            "E:Contoso.Library.Client.Sent",
            "M:Contoso.Library.Client.Send(System.String)",
            "M:Contoso.Library.Client.Send(Contoso.Library.Message,System.Int32)",
            "M:Contoso.Library.Client.Send(System.ReadOnlySpan{System.Byte})",
            "M:Contoso.Library.Client.Convert``1(System.Object)",
            "M:Contoso.Library.Client.OnSent(Contoso.Library.Message)",
            "M:Contoso.Library.Client.System#IDisposable#Dispose",
            "M:Contoso.Library.Client.System#Collections#IEnumerable#GetEnumerator",
            "M:Contoso.Library.Client.op_Addition(Contoso.Library.Client,Contoso.Library.Client)",
            "M:Contoso.Library.Client.op_Implicit(System.String)~Contoso.Library.Client",
            "M:Contoso.Library.Client.op_Explicit(Contoso.Library.Client)~System.String",
            "T:Contoso.Library.Message",
            "M:Contoso.Library.Message.Deconstruct(System.String@)",
            "M:Contoso.Library.Message.PrintMembers(System.Text.StringBuilder)",
            "T:Contoso.Library.Point",
            "T:Contoso.Library.MessageKind",
            "F:Contoso.Library.MessageKind.Binary",
            "T:Contoso.Library.MessageHandler",
            "T:Contoso.Library.IChannel`1",
            "M:Contoso.Library.IChannel`1.Write(`0)",
            "M:Contoso.Library.ClientExtensions.SendTwice(Contoso.Library.Client,System.String)",
            "M:Contoso.Library.Base.Protected",
            "T:Contoso.Library.Collections.Tree`1",
            "M:Contoso.Library.Collections.Tree`1.Map``1(System.Func{`0,``0})",
            "T:Contoso.Library.Collections.Tree`1.Node",
            "T:Contoso.Library.Collections.Tree`1.Node.Child`1",
        ];
        Assert.Superset(expected, documented);

        string[] excluded =
        [
            "M:Contoso.Library.Client.Hidden",
            "T:Contoso.Library.Initializer",
            "M:Contoso.Library.Client.get_Name",
            "P:Contoso.Library.Message.EqualityContract",
            "M:Contoso.Library.MessageHandler.Invoke(Contoso.Library.Message)",
            "M:Contoso.Library.Sealed.Protected",
        ];
        Assert.All(excluded, id => Assert.DoesNotContain(id, documented));
    }

    [Fact]
    public async Task Declarations_HaveNoImplementationStubs()
    {
        var model = await ReadAsync();
        string Format(string id) => DeclarationFormatter.Format(Assert.NotNull(model.Assembly.FindSymbolByDocumentationId(id))).Text;

        Assert.Equal("public void Send(Message message, int retryCount = 3)", Format("M:Contoso.Library.Client.Send(Contoso.Library.Message,System.Int32)"));
        Assert.Equal("public T Convert<T>(object? value) where T : class, new()", Format("M:Contoso.Library.Client.Convert``1(System.Object)"));
        Assert.Equal("public Message? this[string key] { get; set; }", Format("P:Contoso.Library.Client.Item(System.String)"));
        Assert.Equal("public static implicit operator Client(string name)", Format("M:Contoso.Library.Client.op_Implicit(System.String)~Contoso.Library.Client"));
        Assert.Equal("void IDisposable.Dispose()", Format("M:Contoso.Library.Client.System#IDisposable#Dispose"));
        Assert.Equal("public const int DefaultPort = 443", Format("F:Contoso.Library.Client.DefaultPort"));
        Assert.Equal("[Obsolete(\"Use Client instead.\")]\npublic class Legacy", Format("T:Contoso.Library.Legacy"));
        Assert.Equal("public class Child<TKey> where TKey : notnull", Format("T:Contoso.Library.Collections.Tree`1.Node.Child`1"));
        Assert.All(model.Assembly.GetAllSymbols(), symbol => Assert.DoesNotContain("throw null", DeclarationFormatter.Format(symbol).Text));
    }

    [Fact]
    public async Task Declarations_ExposeTypeReferenceSegments()
    {
        var model = await ReadAsync();
        var declaration = DeclarationFormatter.Format(Assert.NotNull(model.Assembly.FindSymbolByDocumentationId("M:Contoso.Library.Client.Send(Contoso.Library.Message,System.Int32)")));

        var segment = Assert.Single(declaration.Segments, s => s.Text == "Message");
        Assert.Equal(PublicApiDeclarationSegmentKind.TypeName, segment.Kind);
        var reference = Assert.IsType<PublicApiNamedTypeReference>(segment.TypeReference);
        Assert.Equal("T:Contoso.Library.Message", reference.DocumentationId);
        Assert.Equal("Contoso.Library", reference.AssemblyName);
    }

    [Fact]
    public async Task Provenance_ScopeAndOrigin()
    {
        var model = await ReadAsync();
        var symbol = Assert.NotNull(model.Assembly.FindSymbolByDocumentationId("M:Contoso.Library.Client.Send(System.String)"));

        Assert.Equal("Contoso.Library/Contoso.Library", model.Scope);
        Assert.Equal(new PublicApiSymbolIdentity("Contoso.Library/Contoso.Library", "M:Contoso.Library.Client.Send(System.String)"), symbol.Identity);
        Assert.Equal("net8.0", model.TargetFramework);
        Assert.Equal("lib/net8.0/Contoso.Library.dll", model.Asset.AssemblyEntry);
        Assert.NotNull(symbol.Origin);
        Assert.Equal(model.Assembly.Module.ModuleVersionId, symbol.Origin.ModuleVersionId);
        Assert.NotEqual(0, symbol.Origin.MetadataToken);
    }

    [Fact]
    public async Task SameDocumentationIdInDifferentPackages_HasDifferentIdentities()
    {
        var first = CreateKitchenSinkPackage("Contoso.First");
        var second = CreateKitchenSinkPackage("Contoso.Second");
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(first)}}, {{TestBuild.PackageReference(second)}} ] }""", [first, second]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());

        var identities = build.Context.ApiAssemblies
            .Where(a => a.TargetFramework == "net8.0")
            .Select(a => a.Assembly.FindSymbolByDocumentationId("T:Contoso.Library.Client")!.Identity)
            .ToList();
        Assert.HasCount(2, identities);
        Assert.NotEqual(identities[0], identities[1]);
        Assert.Equal(["contoso.first", "contoso.second"], build.Context.ApiAssemblies.Select(a => a.ProductId).Distinct());
    }

    [Fact]
    public async Task Inspection_NeverExecutesPackageCode()
    {
        var model = await ReadAsync();

        Assert.NotNull(model.Assembly.FindType("Contoso.Library.Client"));
        Assert.Null(Environment.GetEnvironmentVariable("DOCGEN_FIXTURE_EXECUTED"));
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "Contoso.Library");
    }

    [Fact]
    public async Task Models_RemainUsableAfterTheArchiveIsDisposed()
    {
        var package = CreateKitchenSinkPackage();
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());

        // The reader closed the archive: the package file can be deleted and the model is still complete
        File.Delete(build.Context.Packages[0].Path);
        var model = build.Context.ApiAssemblies.Single(a => a.TargetFramework == "net8.0");
        Assert.NotEmpty(model.Assembly.FindType("Contoso.Library.Client")!.Members);
    }

    [Fact]
    public async Task ApiFilter_IsApplied()
    {
        var package = CreateKitchenSinkPackage();
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ], "api": { "exclude": [ "N:Contoso.Library.Collections", "M:Contoso.Library.Client.Send" ] } }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());
        var policy = new ApiSymbolPolicy(build.Context.Configuration.ApiFilter);
        var model = build.Context.ApiAssemblies.Single(a => a.TargetFramework == "net8.0");

        Assert.False(policy.IsDocumented(model.Assembly.FindSymbolByDocumentationId("T:Contoso.Library.Collections.Tree`1.Node")!));
        Assert.False(policy.IsDocumented(model.Assembly.FindSymbolByDocumentationId("M:Contoso.Library.Client.Send(System.String)")!));
        Assert.True(policy.IsDocumented(model.Assembly.FindSymbolByDocumentationId("M:Contoso.Library.Client.Convert``1(System.Object)")!));
    }

    private static async Task<ApiAssemblyModel> ReadAsync()
    {
        var package = CreateKitchenSinkPackage();
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage());
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG2012");
        return build.Context.ApiAssemblies.Single(a => a.TargetFramework == "net8.0");
    }
}
