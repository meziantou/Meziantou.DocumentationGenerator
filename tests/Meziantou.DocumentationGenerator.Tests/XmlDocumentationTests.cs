using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.DocumentationGenerator.XmlDocs;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class XmlDocumentationTests
{
    private const string Source = """
        namespace Contoso.Docs
        {
            public class Widget
            {
                public void Send(string text) { }
                public void Send(int value) { }
                public T Get<T>(string key) => default!;
                public class Nested<TItem> { public void Add(TItem item) { } }
            }
        }
        """;

    private static readonly Lazy<CompiledAssembly> Assembly = new(() => TestCompiler.Compile("Contoso.Docs", Source));

    private const string FullXml = """
        <?xml version="1.0"?>
        <doc>
          <assembly><name>Contoso.Docs</name></assembly>
          <members>
            <member name="T:Contoso.Docs.Widget">
              <summary>
              A widget with <c>inline code</c>, a <see cref="M:Contoso.Docs.Widget.Send(System.String)"/> link,
              a <see cref="T:Contoso.Docs.Widget.Nested`1">custom text</see> link, <see langword="null"/>,
              an <see href="https://example.com/a?b=1&amp;c=2">external link</see>, Unicode text (café, 日本語) and &lt;script&gt;alert(1)&lt;/script&gt;.
              <para>Second paragraph with <b>bold</b> and <i>italic</i>.</para>
              </summary>
              <remarks>
              <list type="bullet"><item><description>First</description></item><item><term>Term</term><description>Second</description></item></list>
              <list type="number"><item>One</item><item>Two</item></list>
              <list type="table"><listheader><term>Name</term><description>Value</description></listheader><item><term>a</term><description>1</description></item></list>
              <code>
                  var widget = new Widget();
                  if (widget is not null)
                      widget.Send("text");
              </code>
              <code language="xml"><![CDATA[<root attr="1" />]]></code>
              </remarks>
              <example>Example text <code>Inline()</code>.</example>
              <seealso cref="T:System.String"/>
              <seealso href="https://example.com/seealso">See also</seealso>
            </member>
            <member name="M:Contoso.Docs.Widget.Send(System.String)">
              <summary>Sends <paramref name="text"/>.</summary>
              <param name="text">The text.</param>
              <exception cref="T:System.ArgumentNullException"><paramref name="text"/> is null.</exception>
            </member>
            <member name="M:Contoso.Docs.Widget.Send(System.Int32)">
              <summary>Sends a number.</summary>
              <param name="value">The value.</param>
              <param name="missing">Does not exist.</param>
            </member>
            <member name="M:Contoso.Docs.Widget.Get``1(System.String)">
              <summary>Gets a <typeparamref name="T"/> for <paramref name="nope"/>.</summary>
              <summary>Duplicate summary.</summary>
              <typeparam name="T">The type.</typeparam>
              <returns>The <see href="javascript:alert(1)">value</see>. <script>alert(2)</script><unknowntag>kept text</unknowntag></returns>
            </member>
            <member name="M:Contoso.Docs.Widget.Nested`1.Add(`0)">
              <inheritdoc cref="M:Contoso.Docs.Widget.Send(System.String)"/>
              <include file="docs.xml" path="doc/member[@name='Add']/*"/>
            </member>
            <member name="M:Contoso.Docs.Widget.Internal">
              <summary>Internal member.</summary>
            </member>
          </members>
        </doc>
        """;

    [Fact]
    public async Task RendersEverySupportedElement()
    {
        await using var build = await BuildAsync(FullXml);
        var html = Render(build, "T:Contoso.Docs.Widget", c => c.Summary!);

        Assert.Contains("<p>A widget with <code>inline code</code>, a <a href=\"/api/contoso.docs/Contoso.Docs.Widget.html#Send(System.String)\"><code>Widget.Send</code></a> link,", html);
        Assert.Contains("a <a href=\"/api/contoso.docs/Contoso.Docs.Widget.Nested-1.html\">custom text</a> link, <code>null</code>,", html);
        Assert.Contains("an <a href=\"https://example.com/a?b=1&amp;c=2\">external link</a>", html);
        Assert.Contains("Unicode text (café, 日本語)", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("<p>Second paragraph with <strong>bold</strong> and <em>italic</em>.</p>", html);

        var remarks = Render(build, "T:Contoso.Docs.Widget", c => c.Remarks!);
        Assert.Contains("<ul><li>First</li><li><strong>Term</strong> – Second</li></ul>", remarks);
        Assert.Contains("<ol><li>One</li><li>Two</li></ol>", remarks);
        Assert.Contains("<table><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>a</td><td>1</td></tr></tbody></table>", remarks);
        Assert.Contains("<pre><code class=\"hljs language-csharp\"><span class=\"hljs-keyword\">var</span> widget", remarks);
        Assert.Contains("\n    widget.Send(", remarks);
        Assert.Contains("<pre><code class=\"hljs language-xml\">", remarks);
        Assert.DoesNotContain("<root", remarks);

        var example = Render(build, "T:Contoso.Docs.Widget", c => c.Examples[0]);
        Assert.Equal("<p>Example text <code>Inline()</code>.</p>", example);
    }

    [Fact]
    public async Task ParametersExceptionsAndReferences()
    {
        await using var build = await BuildAsync(FullXml);
        var comment = GetComment(build, "M:Contoso.Docs.Widget.Send(System.String)");

        Assert.Equal("text", Assert.Single(comment.Parameters).Name);
        var exception = Assert.Single(comment.Exceptions);
        Assert.Equal("T:System.ArgumentNullException", exception.Name);
        Assert.Equal("<code class=\"paramref\">text</code> is null.", Render(build, "M:Contoso.Docs.Widget.Send(System.String)", _ => exception.Content, inline: true));

        // Overloads map to their own entries
        Assert.Equal("<p>Sends a number.</p>", Render(build, "M:Contoso.Docs.Widget.Send(System.Int32)", c => c.Summary!));
    }

    [Fact]
    public async Task InvalidContent_IsReported()
    {
        await using var build = await BuildAsync(FullXml);
        _ = GetComment(build, "M:Contoso.Docs.Widget.Send(System.Int32)");
        var get = Render(build, "M:Contoso.Docs.Widget.Get``1(System.String)", c => c.Returns!);

        Assert.Equal("<p>The value. alert(2)kept text</p>", get);
        var diagnostics = build.Diagnostics;
        Assert.Contains(diagnostics, d => d.Code == "DG6007" && d.Message.Contains("'missing'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG6007" && d.Message.Contains("'nope'", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG6008" && d.Message.Contains("<summary>", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG4008" && d.Severity == Diagnostics.DiagnosticSeverity.Error && d.Message.Contains("javascript:", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG6006" && d.Message.Contains("<unknowntag>", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "DG6002" && d.Message.Contains("M:Contoso.Docs.Widget.Internal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InheritDocAndIncludeDirectives_AreRetained()
    {
        await using var build = await BuildAsync(FullXml);
        var comment = GetComment(build, "M:Contoso.Docs.Widget.Nested`1.Add(`0)");

        Assert.NotNull(comment.InheritDoc);
        Assert.Single(comment.Includes);
    }

    [Fact]
    public async Task ExternalEntities_AreNotResolved()
    {
        await using var temp = Meziantou.Framework.TemporaryDirectory.Create();
        var secret = temp.CreateTextFile("secret.txt", "TOP-SECRET");
        var xml = $"""
            <?xml version="1.0"?>
            <!DOCTYPE doc [ <!ENTITY xxe SYSTEM "file://{secret.Value.Replace('\\', '/')}"> ]>
            <doc><members><member name="T:Contoso.Docs.Widget"><summary>&xxe;</summary></member></members></doc>
            """;
        await using var build = await BuildAsync(xml);

        Assert.True(GetComment(build, "T:Contoso.Docs.Widget").IsEmpty);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG6001");
        Assert.DoesNotContain(build.Diagnostics, d => d.Message.Contains("TOP-SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MalformedXml_FollowsSeverityPolicy()
    {
        await using var build = await BuildAsync("<doc><members><member name=\"T:Contoso.Docs.Widget\"><summary>unclosed</members></doc>", extra: """ "diagnostics": { "severity": { "DG6001": "error" } }, """, expectSuccess: false);

        var diagnostic = Assert.Single(build.Diagnostics, d => d.Code == "DG6001");
        Assert.Equal(Diagnostics.DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task MissingDocumentation_IsEmpty()
    {
        await using var build = await BuildAsync("<doc><members /></doc>");

        Assert.True(GetComment(build, "M:Contoso.Docs.Widget.Send(System.String)").IsEmpty);
    }

    [Fact]
    public async Task DocumentationVariants_PerTargetFramework()
    {
        var package = new TestPackageBuilder("Contoso.Docs", "1.0.0")
            .AddFile("README.md", "x")
            .AddFile("lib/net8.0/Contoso.Docs.dll", Assembly.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Docs.xml", """<doc><members><member name="T:Contoso.Docs.Widget"><summary>net8.0 docs</summary></member><member name="M:Contoso.Docs.Widget.Send(System.String)"><summary>Same</summary></member></members></doc>""")
            .AddFile("lib/netstandard2.0/Contoso.Docs.dll", Assembly.Value.Assembly)
            .AddFile("lib/netstandard2.0/Contoso.Docs.xml", """<doc><members><member name="T:Contoso.Docs.Widget"><summary>netstandard2.0 docs</summary></member><member name="M:Contoso.Docs.Widget.Send(System.String)"><summary>Same</summary></member></members></doc>""");
        await using var build = TestBuild.Create($$"""{ "version": 1, "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new XmlDocumentationStage());

        var widget = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("T:Contoso.Docs.Widget"));
        var variants = build.Context.Documentation.GetVariants(widget);
        Assert.Equal([["net8.0"], ["netstandard2.0"]], variants.Select(v => v.TargetFrameworks.ToArray()));

        var send = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("M:Contoso.Docs.Widget.Send(System.String)"));
        Assert.Equal(["net8.0", "netstandard2.0"], Assert.Single(build.Context.Documentation.GetVariants(send)).TargetFrameworks);
    }

    private static DocumentationComment GetComment(TestBuild build, string documentationId)
    {
        var symbol = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId(documentationId));
        var variant = Assert.Single(build.Context.Documentation.GetVariants(symbol));
        return variant.Comment;
    }

    private static string Render(TestBuild build, string documentationId, Func<DocumentationComment, System.Xml.Linq.XElement> section, bool inline = false)
    {
        var comment = GetComment(build, documentationId);
        var renderer = new XmlDocRenderer(build.Context.Links!, new HighlightingService(build.Context.Diagnostics), build.Context.Diagnostics);
        var context = new XmlRenderContext(new LinkContext(comment.Scope, "contoso.docs"), comment.SourcePath, documentationId);
        return inline ? renderer.RenderInline(section(comment), context) : renderer.RenderBlock(section(comment), context);
    }

    private static async Task<TestBuild> BuildAsync(string xml, string extra = "", bool expectSuccess = true)
    {
        var package = new TestPackageBuilder("Contoso.Docs", "1.0.0")
            .AddFile("README.md", "x")
            .AddFile("lib/net8.0/Contoso.Docs.dll", Assembly.Value.Assembly)
            .AddFile("lib/net8.0/Contoso.Docs.xml", xml);
        var build = TestBuild.Create($$"""{ "version": 1, {{extra}} "packages": [ {{TestBuild.PackageReference(package)}} ] }""", [package]);
        IBuildStage[] stages = [new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage(), new XmlDocumentationStage()];
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
