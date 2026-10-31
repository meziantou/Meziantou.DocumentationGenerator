using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.DocumentationGenerator.XmlDocs;
using Document = Meziantou.Framework.WebPlatform.Dom.Document;
using Element = Meziantou.Framework.WebPlatform.Dom.Element;

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

                    /// <summary>The last item.</summary>
                    public TItem Last { get => list[^1]; set => list[^1] = value; }
                }

                extension(int)
                {
                    /// <summary>Creates a value.</summary>
                    public static int CreateDefault() => 0;

                    /// <summary>The zero value.</summary>
                    public static int Zero => 0;
                }

                /// <summary>Not an extension property.</summary>
                /// <param name="value">The value.</param>
                /// <param name="other">The other value.</param>
                public static int get_Zero(int value, int other) => value + other;
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
    public async Task ExtensionBlockReferences_ResolveToTheExtensionMember()
    {
        await using var build = await BuildAsync();

        Assert.Equal("<p>See <a href=\"/api/Contoso.Ext.StringExtensions.html#string.Repeat(System.Int32)\"><code>string.Repeat</code></a>.</p>", RenderSummary(build, "T:Contoso.Ext.Consumer"));

        var alias = GetComment(build, "M:Contoso.Ext.Consumer.RepeatAlias(System.String,System.Int32)");
        Assert.Equal("<p>Repeats <code class=\"paramref\">text</code> <code class=\"paramref\">times</code> times.</p>", RenderSummary(build, "M:Contoso.Ext.Consumer.RepeatAlias(System.String,System.Int32)"));
        Assert.Equal(["text", "times"], alias.Parameters.Select(p => p.Name));
    }

    [Fact]
    public async Task ImplementationIds_ResolveToTheExtensionMember()
    {
        await using var build = await BuildAsync();

        var property = Assert.IsType<ApiMemberModel>(Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("M:Contoso.Ext.StringExtensions.get_Zero")));
        Assert.Equal(ApiMemberCategory.Property, property.Category);
        Assert.Same(property, Assert.Single(build.Context.ApiCatalog.FindByDocumentationId(property.DocumentationId)));
        Assert.StartsWith("P:Contoso.Ext.StringExtensions.<G>$", property.DocumentationId, StringComparison.Ordinal);

        var last = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("M:Contoso.Ext.StringExtensions.set_Last``1(System.Collections.Generic.List{``0},``0)"));
        Assert.Same(last, Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("M:Contoso.Ext.StringExtensions.get_Last``1(System.Collections.Generic.List{``0})")));

        // A hand-written method with an accessor-like name is a method
        var method = Assert.IsType<ApiMemberModel>(Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("M:Contoso.Ext.StringExtensions.get_Zero(System.Int32,System.Int32)")));
        Assert.Equal(ApiMemberCategory.Method, method.Category);
        Assert.Null(method.ExtensionBlock);
    }

    [Fact]
    public async Task TypePage_ShowsExtensionMembersAsDeclared()
    {
        await using var site = await SiteBuilder.BuildAsync([CreatePackage()]);
        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        var document = HtmlDocumentLoader.Parse(site.ReadFile("api/Contoso.Ext.StringExtensions.html"));

        // Extension properties are properties, with a single entry for the getter and the setter, and every member shows its receiver
        Assert.Equal(["Properties", "Methods"], document.QuerySelectorAll<Element>("article > section > h2").Select(h => h.TextContent));
        Assert.Equal(["string.DoubleLength", "List<TItem>.Last", "int.Zero"], GetTableNames(document, "section-property"));
        Assert.Equal(["string.Convert<T>(T)", "int.CreateDefault()", "List<TItem>.FirstItem()", "string.Repeat(int)", "get_Zero(int, int)"], GetTableNames(document, "section-method"));

        // Anchors use the receiver type; the anchors of the implementation methods still lead to the member
        var zero = document.GetElementById("int.Zero")!;
        Assert.Equal("extension(int)\n{\n    public static int Zero { get; }\n}", zero.QuerySelector("pre.declaration")!.TextContent);
        Assert.Contains("The zero value.", zero.TextContent, StringComparison.Ordinal);
        Assert.Same(zero, document.GetElementById("get_Zero")!.ParentElement);

        var last = document.GetElementById("List<TItem>.Last")!;
        Assert.Equal("extension<TItem>(List<TItem> list)\n{\n    public TItem Last { get; set; }\n}", last.QuerySelector("pre.declaration")!.TextContent);
        Assert.Same(last, document.GetElementById("get_Last``1(System.Collections.Generic.List{``0})")!.ParentElement);
        Assert.Same(last, document.GetElementById("set_Last``1(System.Collections.Generic.List{``0},``0)")!.ParentElement);
        Assert.Equal(["TItem", "list List<TItem>"], last.QuerySelectorAll<Element>("dl.parameters dt").Select(d => d.TextContent));

        // Instance members document the receiver of their block
        var repeat = document.GetElementById("string.Repeat(System.Int32)")!;
        Assert.Same(repeat, document.GetElementById("Repeat(System.String,System.Int32)")!.ParentElement);
        Assert.Equal(["value string", "count int"], repeat.QuerySelectorAll<Element>("dl.parameters dt").Select(d => d.TextContent));
        Assert.Contains("The receiver.", repeat.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<G>$", document.DocumentElement!.OuterHtml, StringComparison.Ordinal);

        // Search uses the names as written by consumers
        var search = site.ReadFile("_assets/search-index.json");
        Assert.Contains("\"int.Zero\"", search, StringComparison.Ordinal);
        Assert.Contains("\"string.Repeat\"", search, StringComparison.Ordinal);
        Assert.DoesNotContain("get_Last", search, StringComparison.Ordinal);

        static List<string> GetTableNames(Document document, string sectionId)
        {
            return [.. document.GetElementById(sectionId)!.ParentElement!.QuerySelectorAll<Element>("table.api-table tbody td:first-child").Select(td => td.TextContent ?? "")];
        }
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

    private static TestPackageBuilder CreatePackage()
    {
        return new TestPackageBuilder("Contoso.Ext", "1.0.0")
            .AddFile("README.md", "x")
            .AddFile("lib/net10.0/Contoso.Ext.dll", Assembly.Value.Assembly)
            .AddFile("lib/net10.0/Contoso.Ext.xml", Assembly.Value.Xml);
    }

    private static async Task<TestBuild> BuildAsync()
    {
        var package = CreatePackage();
        var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new XmlDocumentationStage(), new InheritanceStage());
        return build;
    }
}
