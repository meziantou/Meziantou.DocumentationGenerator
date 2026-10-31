using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.DocumentationGenerator.XmlDocs;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class InheritanceTests
{
    private const string Source = """
        namespace Contoso.Inherit
        {
            /// <summary>Base summary.</summary>
            /// <remarks>Base remarks with <see cref="Base.Run(int)"/>.</remarks>
            public abstract class Base
            {
                /// <summary>Runs <paramref name="count"/> times.</summary>
                /// <param name="count">The count.</param>
                /// <returns>The result.</returns>
                public abstract int Run(int count);

                /// <summary>Virtual member.</summary>
                public virtual void Virtual() { }
            }

            /// <inheritdoc/>
            public class Derived : Base
            {
                /// <inheritdoc/>
                /// <returns>Local returns win.</returns>
                public override int Run(int times) => 0;

                /// <inheritdoc/>
                public override void Virtual() { }
            }

            /// <summary>Generic base.</summary>
            /// <typeparam name="T">The item type.</typeparam>
            public class GenericBase<T>
            {
                /// <summary>Processes <paramref name="value"/>.</summary>
                /// <param name="value">The value.</param>
                public virtual void Process(T value) { }

                /// <summary>Processes a string.</summary>
                public virtual void Process(int value) { }
            }

            /// <summary>Generic derived.</summary>
            public class GenericDerived : GenericBase<string>
            {
                /// <inheritdoc/>
                public override void Process(string item) { }
            }

            /// <summary>First.</summary>
            public interface IFirst
            {
                /// <summary>First Do.</summary>
                void Do();
            }

            /// <summary>Second.</summary>
            public interface ISecond
            {
                /// <summary>Second Do.</summary>
                void Do();
            }

            /// <summary>Multi.</summary>
            public class Multi : IFirst, ISecond
            {
                /// <inheritdoc/>
                public void Do() { }
            }

            /// <summary>Explicit.</summary>
            public class Explicit : ISecond
            {
                /// <inheritdoc/>
                void ISecond.Do() { }
            }

            /// <summary>Explicit cref.</summary>
            public class ExplicitCref
            {
                /// <inheritdoc cref="Base.Run(int)"/>
                public int Other(int x) => 0;

                /// <inheritdoc cref="Base" path="/remarks"/>
                public void Filtered() { }

                /// <inheritdoc cref="Base" path="//remarks"/>
                public void Unsupported() { }
            }

            /// <summary>Cycle A.</summary>
            public class CycleA
            {
                /// <inheritdoc cref="CycleB.M"/>
                public void M() { }
            }

            /// <summary>Cycle B.</summary>
            public class CycleB
            {
                /// <inheritdoc cref="CycleA.M"/>
                public void M() { }
            }

            /// <summary>External.</summary>
            public class External : System.Collections.ObjectModel.Collection<int>
            {
                /// <inheritdoc/>
                protected override void ClearItems() { }
            }

        #if NET8
            /// <summary>Net8 base.</summary>
            public class TfmBase
            {
                /// <summary>Net8 member.</summary>
                public virtual void M() { }
            }
        #else
            /// <summary>Netstandard base.</summary>
            public class TfmBase
            {
                /// <summary>Netstandard member.</summary>
                public virtual void M() { }
            }
        #endif

            /// <summary>Derived per framework.</summary>
            public class TfmDerived : TfmBase
            {
                /// <inheritdoc/>
                public override void M() { }
            }
        }
        """;

    private static readonly Lazy<CompiledAssembly> Assembly = new(() => TestCompiler.Compile("Contoso.Inherit", Source));
    private static readonly Lazy<CompiledAssembly> AssemblyNet8 = new(() => TestCompiler.Compile("Contoso.Inherit", Source, new CompileOptions { Defines = ["NET8"] }));

    [Fact]
    public async Task ImplicitInheritance_FromBaseTypeAndOverriddenMember()
    {
        await using var build = await BuildAsync();

        Assert.Equal("<p>Base summary.</p>", RenderSummary(build, "T:Contoso.Inherit.Derived"));
        Assert.Equal("<p>Virtual member.</p>", RenderSummary(build, "M:Contoso.Inherit.Derived.Virtual"));

        var run = GetComment(build, "M:Contoso.Inherit.Derived.Run(System.Int32)");
        Assert.Equal("<p>Runs <code class=\"paramref\">times</code> times.</p>", RenderSummary(build, "M:Contoso.Inherit.Derived.Run(System.Int32)"));
        Assert.Equal("times", Assert.Single(run.Parameters).Name);
        Assert.Contains("Local returns win.", run.Returns!.Value);
        Assert.Null(run.InheritDoc);
    }

    [Fact]
    public async Task InheritedLinks_ResolveInTheirOriginalContext()
    {
        await using var build = await BuildAsync();
        var remarks = Render(build, "T:Contoso.Inherit.Derived", c => c.Remarks!);

        Assert.Contains("<a href=\"/api/Contoso.Inherit.Base.html#Run(System.Int32)\"><code>Base.Run</code></a>", remarks);
    }

    [Fact]
    public async Task GenericBase_IsSubstituted()
    {
        await using var build = await BuildAsync();

        Assert.Equal("<p>Processes <code class=\"paramref\">item</code>.</p>", RenderSummary(build, "M:Contoso.Inherit.GenericDerived.Process(System.String)"));
    }

    [Fact]
    public async Task Interfaces()
    {
        await using var build = await BuildAsync();

        Assert.Equal("<p>First Do.</p>", RenderSummary(build, "M:Contoso.Inherit.Multi.Do"));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG6010");
        Assert.Equal("<p>Second Do.</p>", RenderSummary(build, "M:Contoso.Inherit.Explicit.Contoso#Inherit#ISecond#Do"));
    }

    [Fact]
    public async Task ExplicitCrefAndPath()
    {
        await using var build = await BuildAsync();

        Assert.Equal("<p>Runs <code class=\"paramref\">x</code> times.</p>", RenderSummary(build, "M:Contoso.Inherit.ExplicitCref.Other(System.Int32)"));
        var filtered = GetComment(build, "M:Contoso.Inherit.ExplicitCref.Filtered");
        Assert.Null(filtered.Summary);
        Assert.NotNull(filtered.Remarks);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG6009" && d.Location?.Symbol == "M:Contoso.Inherit.ExplicitCref.Unsupported");
    }

    [Fact]
    public async Task Cycles_AreReported()
    {
        await using var build = await BuildAsync();

        var comment = GetComment(build, "M:Contoso.Inherit.CycleA.M");
        Assert.NotNull(comment.InheritanceNote);
        var diagnostic = build.Diagnostics.First(d => d.Code == "DG6004" && d.Location?.TargetFramework == "net8.0");
        Assert.Contains("M:Contoso.Inherit.CycleA.M", diagnostic.Message);
        Assert.Contains("M:Contoso.Inherit.CycleB.M", diagnostic.Message);
    }

    [Fact]
    public async Task MissingDependencyDocumentation_IsNotSilentlyEmpty()
    {
        await using var build = await BuildAsync();

        var comment = GetComment(build, "M:Contoso.Inherit.External.ClearItems");
        Assert.True(comment.IsEmpty);
        Assert.Contains("not part of the configured packages", comment.InheritanceNote);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG6003" && d.Location?.Symbol == "M:Contoso.Inherit.External.ClearItems");
    }

    [Fact]
    public async Task PerFrameworkRelationships()
    {
        await using var build = await BuildAsync();
        var symbol = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("M:Contoso.Inherit.TfmDerived.M"));
        var variants = build.Context.Documentation.GetVariants(symbol);

        Assert.Equal(["Net8 member.", "Netstandard member."], variants.Select(v => v.Comment.Summary!.Value));
        Assert.Equal([["net8.0"], ["netstandard2.0"]], variants.Select(v => v.TargetFrameworks.ToArray()));
    }

    [Fact]
    public async Task Resolution_IsIndependentOfPackageOrder()
    {
        var other = new TestPackageBuilder("Contoso.Other", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.Other.dll", TestCompiler.Compile("Contoso.Other", "namespace Other { /// <summary>O.</summary>\npublic class O { } }").Assembly);
        await using var first = await BuildAsync(extraPackages: [other]);
        await using var second = await BuildAsync(extraPackages: [other], reverse: true);

        string Describe(TestBuild build) => string.Join('\n', build.Context.ApiCatalog.Assemblies.Where(a => a.Package.Id == "Contoso.Inherit").SelectMany(a => a.GetAllTypes()).SelectMany(t => new Api.ApiSymbolModel[] { t }.Concat(t.Members))
            .Select(s => s.DocumentationId + "=" + string.Join("|", build.Context.Documentation.GetVariants(s).Select(v => v.Comment.GetComparisonKey()))));
        Assert.Equal(Describe(first), Describe(second));
    }

    [Fact]
    public async Task Includes_AreBoundedToThePackage()
    {
        var assembly = TestCompiler.Compile("Contoso.Include", "namespace Contoso.Include { public class Widget { public void Run() { } public void Missing() { } public void Escape() { } public void Unsupported() { } } }");
        var package = new TestPackageBuilder("Contoso.Include", "1.0.0")
            .AddFile("README.md", "x")
            .AddFile("lib/net8.0/Contoso.Include.dll", assembly.Assembly)
            .AddFile("lib/net8.0/Contoso.Include.xml", """
                <doc><members>
                  <member name="M:Contoso.Include.Widget.Run"><include file="docs/extra.xml" path="doc/item[@name='Run']/*" /></member>
                  <member name="M:Contoso.Include.Widget.Missing"><include file="docs/missing.xml" path="doc/item" /></member>
                  <member name="M:Contoso.Include.Widget.Escape"><include file="../../../secret.xml" path="doc/item" /></member>
                  <member name="M:Contoso.Include.Widget.Unsupported"><include file="docs/extra.xml" path="//item" /></member>
                </members></doc>
                """)
            .AddFile("lib/net8.0/docs/extra.xml", """<doc><item name="Run"><summary>Included summary.</summary><remarks>Included remarks.</remarks></item></doc>""");
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new XmlDocumentationStage(), new InheritanceStage());

        var run = GetComment(build, "M:Contoso.Include.Widget.Run");
        Assert.Equal("Included summary.", run.Summary!.Value);
        Assert.Equal("Included remarks.", run.Remarks!.Value);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG6005" && d.Location?.Symbol == "M:Contoso.Include.Widget.Missing");
        Assert.Contains(build.Diagnostics, d => d.Code == "DG5009" && d.Location?.Symbol == "M:Contoso.Include.Widget.Escape");
        Assert.Contains(build.Diagnostics, d => d.Code == "DG6009" && d.Location?.Symbol == "M:Contoso.Include.Widget.Unsupported");
    }

    [Fact]
    public async Task DocumentationIssuesOfReferences_AreNotReported()
    {
        var referenceAssembly = TestCompiler.Compile("Contoso.Reference", """
            namespace Contoso.Reference
            {
                public class Base
                {
                    public virtual void Run(int count) { }
                    public void Cycle() { }
                    public void Unresolved() { }
                    public void Include() { }
                    public void Escape() { }
                }
            }
            """);
        var undocumented = TestCompiler.Compile("Contoso.Reference.Undocumented", "namespace Contoso.Reference.Undocumented { public class Widget { } }");
        var reference = new TestPackageBuilder("Contoso.Reference", "1.0.0")
            .AddFile("lib/net8.0/Contoso.Reference.dll", referenceAssembly.Assembly)
            .AddFile("lib/net8.0/Contoso.Reference.xml", """
                <doc><members>
                  <member name="T:Contoso.Reference.Base"><summary>Base.</summary><summary>Duplicate.</summary><unknown>x</unknown></member>
                  <member name="T:Contoso.Reference.Base"><summary>Duplicate entry.</summary></member>
                  <member name="M:Contoso.Reference.Base.Run(System.Int32)"><summary>Runs <paramref name="missing"/>.</summary><param name="other">x</param></member>
                  <member name="M:Contoso.Reference.Base.Cycle"><inheritdoc cref="M:Contoso.Reference.Base.Cycle"/></member>
                  <member name="M:Contoso.Reference.Base.Unresolved"><inheritdoc/></member>
                  <member name="M:Contoso.Reference.Base.Include"><include file="docs/missing.xml" path="doc/item" /></member>
                  <member name="M:Contoso.Reference.Base.Escape"><include file="../../../secret.xml" path="doc/item" /></member>
                  <member name="M:Contoso.Reference.Internal.Member"><summary>Internal.</summary></member>
                </members></doc>
                """)
            .AddFile("lib/net8.0/Contoso.Reference.Undocumented.dll", undocumented.Assembly);
        var consumerAssembly = TestCompiler.Compile("Contoso.Consumer", """
            namespace Contoso.Consumer
            {
                /// <summary>Derived.</summary>
                public class Derived : Contoso.Reference.Base
                {
                    /// <inheritdoc/>
                    public override void Run(int times) { }
                }
            }
            """, new CompileOptions { References = [referenceAssembly] });
        var consumer = new TestPackageBuilder("Contoso.Consumer", "1.0.0")
            .AddFile("README.md", "x")
            .AddFile("lib/net8.0/Contoso.Consumer.dll", consumerAssembly.Assembly)
            .AddFile("lib/net8.0/Contoso.Consumer.xml", consumerAssembly.Xml);
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(consumer)}} ], "references": [ {{TestBuild.PackageReference(reference)}} ] }""", [consumer, reference]);
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new XmlDocumentationStage(), new InheritanceStage());

        Assert.Contains("Runs", GetComment(build, "M:Contoso.Consumer.Derived.Run(System.Int32)").Summary!.Value, StringComparison.Ordinal);
        var referenceDiagnostics = build.Diagnostics.Where(d => d.Location?.Package?.StartsWith("Contoso.Reference", StringComparison.Ordinal) == true || d.Location?.FilePath?.StartsWith("Contoso.Reference", StringComparison.Ordinal) == true).ToList();
        Assert.DoesNotContain(referenceDiagnostics, d => d.Code.StartsWith("DG6", StringComparison.Ordinal) || d.Code == "DG3002");

        // Unsafe input is reported whatever the package
        Assert.Contains(referenceDiagnostics, d => d.Code == "DG5009" && d.Location?.Symbol == "M:Contoso.Reference.Base.Escape");
    }

    private static DocumentationComment GetComment(TestBuild build, string documentationId)
    {
        var symbol = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId(documentationId));
        return build.Context.Documentation.GetVariants(symbol)[0].Comment;
    }

    private static string RenderSummary(TestBuild build, string documentationId) => Render(build, documentationId, c => c.Summary!);

    private static string Render(TestBuild build, string documentationId, Func<DocumentationComment, System.Xml.Linq.XElement> section)
    {
        var comment = GetComment(build, documentationId);
        var renderer = new XmlDocRenderer(build.Context.Links!, new HighlightingService(build.Context.Diagnostics), build.Context.Diagnostics);
        return renderer.RenderBlock(section(comment), new XmlRenderContext(new LinkContext(comment.Scope, null), comment.SourcePath, documentationId));
    }

    private static async Task<TestBuild> BuildAsync(IReadOnlyList<TestPackageBuilder>? extraPackages = null, bool reverse = false)
    {
        var package = new TestPackageBuilder("Contoso.Inherit", "1.0.0")
            .AddFile("README.md", "x")
            .AddFile("lib/net8.0/Contoso.Inherit.dll", AssemblyNet8.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Inherit.xml", AssemblyNet8.Value.Xml)
            .AddFile("lib/netstandard2.0/Contoso.Inherit.dll", Assembly.Value.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Inherit.xml", Assembly.Value.Xml);
        List<TestPackageBuilder> packages = [package, .. extraPackages ?? []];
        if (reverse)
        {
            packages.Reverse();
        }

        var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{string.Join(", ", packages.Select(p => TestBuild.PackageReference(p)))}} ] }""", packages);
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new XmlDocumentationStage(), new InheritanceStage());
        return build;
    }
}
