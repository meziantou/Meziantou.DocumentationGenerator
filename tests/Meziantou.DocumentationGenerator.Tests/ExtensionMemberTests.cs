using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.DocumentationGenerator.XmlDocs;

namespace Meziantou.DocumentationGenerator.Tests;

/// <summary>C# 14 extension members are documented on their extension block, which the compiler emits as a hidden grouping type.</summary>
public sealed class ExtensionMemberTests
{
    private const string Source = """
        namespace Contoso.Ext
        {
            /// <summary>Extensions.</summary>
            public static class StringExtensions
            {
                /// <summary>Block.</summary>
                /// <param name="value">The receiver.</param>
                extension(string value)
                {
                    /// <summary>Repeats <paramref name="value"/> <paramref name="count"/> times.</summary>
                    /// <param name="count">The count.</param>
                    /// <returns>The repeated value.</returns>
                    public string Repeat(int count) => value;

                    /// <summary>Converts the value.</summary>
                    /// <typeparam name="T">The target type.</typeparam>
                    /// <param name="fallback">The fallback.</param>
                    public T Convert<T>(T fallback) => fallback;

                    /// <summary>The doubled length.</summary>
                    /// <value>Twice the length.</value>
                    public int DoubleLength => value.Length * 2;
                }

                /// <typeparam name="TItem">The item type.</typeparam>
                /// <param name="list">The list.</param>
                extension<TItem>(System.Collections.Generic.List<TItem> list)
                {
                    /// <summary>Returns the first item.</summary>
                    public TItem FirstItem() => list[0];
                }

                extension(int)
                {
                    /// <summary>Creates a value.</summary>
                    public static int CreateDefault() => 0;
                }
            }

            /// <summary>See <see cref="StringExtensions.extension(string).Repeat(int)"/>.</summary>
            public static class Consumer
            {
                /// <inheritdoc cref="StringExtensions.extension(string).Repeat(int)"/>
                public static string RepeatAlias(string text, int times) => text;
            }
        }
        """;

    private static readonly Lazy<CompiledAssembly> Assembly = new(() => TestCompiler.Compile("Contoso.Ext", Source));

    [Fact]
    public async Task InstanceMember_InheritsTheDocumentationOfItsExtensionBlock()
    {
        await using var build = await BuildAsync();

        var comment = GetComment(build, "M:Contoso.Ext.StringExtensions.Repeat(System.String,System.Int32)");
        Assert.Null(comment.InheritDoc);
        Assert.Null(comment.InheritanceNote);
        Assert.Equal("<p>Repeats <code class=\"paramref\">value</code> <code class=\"paramref\">count</code> times.</p>", RenderSummary(build, "M:Contoso.Ext.StringExtensions.Repeat(System.String,System.Int32)"));
        Assert.Equal("The repeated value.", comment.Returns!.Value);

        // The receiver is documented on the extension block
        Assert.Equal(["value", "count"], comment.Parameters.Select(p => p.Name));
        Assert.Equal("The receiver.", comment.Parameters[0].Content.Value);
    }

    [Fact]
    public async Task GenericMembersAndBlocks_DocumentTheirTypeParameters()
    {
        await using var build = await BuildAsync();

        var convert = GetComment(build, "M:Contoso.Ext.StringExtensions.Convert``1(System.String,``0)");
        Assert.Equal("Converts the value.", convert.Summary!.Value);
        Assert.Equal(["value", "fallback"], convert.Parameters.Select(p => p.Name));
        Assert.Equal("T", Assert.Single(convert.TypeParameters).Name);

        var first = GetComment(build, "M:Contoso.Ext.StringExtensions.FirstItem``1(System.Collections.Generic.List{``0})");
        Assert.Equal("Returns the first item.", first.Summary!.Value);
        Assert.Equal("The list.", Assert.Single(first.Parameters).Content.Value);
        Assert.Equal("The item type.", Assert.Single(first.TypeParameters).Content.Value);
    }

    [Fact]
    public async Task StaticMembersAndProperties_InheritTheirDocumentation()
    {
        await using var build = await BuildAsync();

        var create = GetComment(build, "M:Contoso.Ext.StringExtensions.CreateDefault");
        Assert.Equal("Creates a value.", create.Summary!.Value);
        Assert.Empty(create.Parameters);

        var getter = GetComment(build, "M:Contoso.Ext.StringExtensions.get_DoubleLength(System.String)");
        Assert.Equal("The doubled length.", getter.Summary!.Value);
        Assert.Equal("Twice the length.", getter.Value!.Value);
    }

    [Fact]
    public async Task ExtensionBlockReferences_ResolveToTheImplementation()
    {
        await using var build = await BuildAsync();

        Assert.Contains("<a href=\"/api/contoso.ext/Contoso.Ext.StringExtensions.html#Repeat(System.String,System.Int32)\">", RenderSummary(build, "T:Contoso.Ext.Consumer"), StringComparison.Ordinal);

        var alias = GetComment(build, "M:Contoso.Ext.Consumer.RepeatAlias(System.String,System.Int32)");
        Assert.Equal("<p>Repeats <code class=\"paramref\">text</code> <code class=\"paramref\">times</code> times.</p>", RenderSummary(build, "M:Contoso.Ext.Consumer.RepeatAlias(System.String,System.Int32)"));
        Assert.Equal(["text", "times"], alias.Parameters.Select(p => p.Name));
    }

    [Fact]
    public async Task ExtensionBlocks_DoNotReportDiagnostics()
    {
        await using var build = await BuildAsync();

        Assert.DoesNotContain(build.Diagnostics, d => d.Code.StartsWith("DG6", StringComparison.Ordinal) && d.Code != "DG6002");
        Assert.DoesNotContain(build.Diagnostics, d => d.Code == "DG6002" && d.Message.Contains("<G>$", StringComparison.Ordinal));
        Assert.DoesNotContain(build.Diagnostics, d => d.Message.Contains("<G>$", StringComparison.Ordinal));
    }

    private static DocumentationComment GetComment(TestBuild build, string documentationId)
    {
        var symbol = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId(documentationId));
        return build.Context.Documentation.GetVariants(symbol)[0].Comment;
    }

    private static string RenderSummary(TestBuild build, string documentationId)
    {
        var comment = GetComment(build, documentationId);
        var renderer = new XmlDocRenderer(build.Context.Links!, new HighlightingService(build.Context.Diagnostics), build.Context.Diagnostics);
        return renderer.RenderBlock(comment.Summary!, new XmlRenderContext(new LinkContext(comment.Scope, null), comment.SourcePath, documentationId));
    }

    private static async Task<TestBuild> BuildAsync()
    {
        var package = new TestPackageBuilder("Contoso.Ext", "1.0.0")
            .AddFile("README.md", "x")
            .AddFile("lib/net10.0/Contoso.Ext.dll", Assembly.Value.Assembly)
            .AddFile("lib/net10.0/Contoso.Ext.xml", Assembly.Value.Xml);
        var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new XmlDocumentationStage(), new InheritanceStage());
        return build;
    }
}
