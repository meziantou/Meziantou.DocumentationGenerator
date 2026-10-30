using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Document = Meziantou.Framework.WebPlatform.Dom.Document;
using Element = Meziantou.Framework.WebPlatform.Dom.Element;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class ApiPageTests
{
    [Fact]
    public async Task GeneratesEveryPageKind()
    {
        await using var site = await BuildKitchenSinkAsync();

        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        string[] expected =
        [
            "index.html",
            "api/contoso.library/index.html",
            "api/contoso.library/namespaces/Contoso.Library.html",
            "api/contoso.library/namespaces/Contoso.Library.Collections.html",
            "api/contoso.library/Contoso.Library.Client.html",
            "api/contoso.library/Contoso.Library.MessageKind.html",
            "api/contoso.library/Contoso.Library.MessageHandler.html",
            "api/contoso.library/Contoso.Library.IChannel-1.html",
            "api/contoso.library/Contoso.Library.Point.html",
            "api/contoso.library/Contoso.Library.Collections.Tree-1.Node.Child-1.html",
        ];
        Assert.All(expected, file => Assert.True(File.Exists(site.Output / file), file));
        Assert.False(File.Exists(site.Output / "contoso.library" / "index.html"));
    }

    [Fact]
    public async Task TypePage_StructureAndMembers()
    {
        await using var site = await BuildKitchenSinkAsync();
        var document = Load(site, "api/contoso.library/Contoso.Library.Client.html");

        Assert.Equal("Client Class", document.QuerySelector("h1")!.TextContent);
        Assert.Equal("Client Class | Test site", document.Title);
        Assert.Equal("A client that sends Message instances.", document.QuerySelector("meta[name=description]")!.GetAttribute("content"));
        Assert.Equal(["Constructors", "Properties", "Indexers", "Methods", "Operators", "Events", "Fields", "Constants"], document.QuerySelectorAll<Element>("article > section > h2").Select(h => h.TextContent));

        // Members have the anchors computed by the link service, and overloads are grouped
        Assert.NotNull(document.GetElementById("Send(System.String)"));
        Assert.NotNull(document.GetElementById("#ctor(System.String)"));
        Assert.NotNull(document.GetElementById("System#IDisposable#Dispose"));
        Assert.NotNull(document.GetElementById("op_Implicit(System.String)~Contoso.Library.Client"));
        var sendGroup = document.GetElementById("Send-overloads")!;
        Assert.Equal(3, sendGroup.QuerySelectorAll<Element>("section.api-member").Count);

        // TFM-specific overload
        var span = document.GetElementById("Send(System.ReadOnlySpan{System.Byte})")!;
        Assert.Equal("net8.0", span.GetAttribute("data-tfms"));
        Assert.Contains("net8.0", span.QuerySelector(".member-availability")!.TextContent);

        // Declarations link the types of the configured packages and contain no implementation
        var send = document.GetElementById("Send(Contoso.Library.Message,System.Int32)")!;
        var declaration = send.QuerySelector("pre.declaration")!;
        Assert.Equal("public void Send(Message message, int retryCount = 3)", declaration.TextContent);
        // Framework types link to Microsoft Learn by default; void is never a link
        Assert.Equal(["Message", "int"], declaration.QuerySelectorAll<Element>("a").Select(a => a.TextContent));
        Assert.Equal(["/api/contoso.library/Contoso.Library.Message.html", "https://learn.microsoft.com/dotnet/api/system.int32"], declaration.QuerySelectorAll<Element>("a").Select(a => a.GetAttribute("href")));
        Assert.DoesNotContain("throw null", document.DocumentElement!.OuterHtml);

        // Documentation sections
        Assert.Contains("The retry count.", send.TextContent);
        var parameters = send.QuerySelectorAll<Element>("dl.parameters dt").Select(d => d.TextContent).ToList();
        Assert.Equal(["message Message", "retryCount int"], parameters);
        Assert.Contains("when sent", document.GetElementById("Send(System.String)")!.TextContent);

        // No raw XML documentation tags leak
        var html = document.QuerySelector("article")!.OuterHtml;
        Assert.DoesNotContain("<summary", html);
        Assert.DoesNotContain("<see ", html);
        Assert.DoesNotContain("<paramref", html);
        Assert.DoesNotContain("cref=", html);
    }

    [Fact]
    public async Task TypePage_KindsAndRelations()
    {
        await using var site = await BuildKitchenSinkAsync();

        var enumPage = Load(site, "api/contoso.library/Contoso.Library.MessageKind.html");
        Assert.Equal("MessageKind Enum", enumPage.QuerySelector("h1")!.TextContent);
        Assert.Equal(["None", "Text", "Binary"], enumPage.QuerySelectorAll<Element>("table.api-table tbody tr td:first-child").Select(c => c.TextContent));
        Assert.Equal("2", enumPage.GetElementById("Binary")!.QuerySelectorAll<Element>("td")[1].TextContent);

        var delegatePage = Load(site, "api/contoso.library/Contoso.Library.MessageHandler.html");
        Assert.Equal("MessageHandler Delegate", delegatePage.QuerySelector("h1")!.TextContent);
        Assert.Contains("The message.", delegatePage.QuerySelector("dl.parameters")!.TextContent);

        var sealedPage = Load(site, "api/contoso.library/Contoso.Library.Sealed.html");
        Assert.Equal("/api/contoso.library/Contoso.Library.Base.html", sealedPage.QuerySelector("dl.api-relations a")!.GetAttribute("href"));
        Assert.Null(sealedPage.GetElementById("Protected"));

        var legacy = Load(site, "api/contoso.library/Contoso.Library.Legacy.html");
        Assert.Equal("Obsolete: Use Client instead.", legacy.QuerySelector(".badge-obsolete")!.TextContent);

        var nested = Load(site, "api/contoso.library/Contoso.Library.Collections.Tree-1.Node.Child-1.html");
        Assert.Equal("Tree<T>.Node.Child<TKey> Class", nested.QuerySelector("h1")!.TextContent);
        Assert.Equal("/api/contoso.library/Contoso.Library.Collections.Tree-1.Node.html", nested.QuerySelectorAll<Element>("dl.api-metadata a")[^1].GetAttribute("href"));
    }

    [Fact]
    public async Task NamespaceAndReferencePages()
    {
        await using var site = await BuildKitchenSinkAsync();

        var ns = Load(site, "api/contoso.library/namespaces/Contoso.Library.html");
        Assert.Equal(["Classes", "Structs", "Interfaces", "Enums", "Delegates"], ns.QuerySelectorAll<Element>("h2:not(#page-toc-title)").Select(h => h.TextContent));
        var modernOnly = ns.QuerySelectorAll<Element>("tr").Single(r => r.TextContent!.Contains("ModernOnly", StringComparison.Ordinal));
        Assert.Equal("net8.0", modernOnly.GetAttribute("data-tfms"));

        var reference = Load(site, "api/contoso.library/index.html");
        Assert.Equal(["Contoso.Library", "Contoso.Library.Collections"], reference.QuerySelectorAll<Element>("tbody td:first-child a").Select(a => a.TextContent));
    }

    [Fact]
    public async Task MultipleFrameworks_ShowDeclarationAndDocumentationVariants()
    {
        var source = """
            namespace Contoso.Variants
            {
                /// <summary>A widget.</summary>
                public class Widget
                {
            #if NET8
                    /// <summary>The name (net8.0).</summary>
                    public string? Name { get; set; }
            #else
                    /// <summary>The name (netstandard2.0).</summary>
                    public string Name { get; set; } = "";
            #endif
                    /// <summary>Common.</summary>
                    public void Common() { }
                }
            }
            """;
        var net8 = TestCompiler.Compile("Contoso.Variants", source, new CompileOptions { Defines = ["NET8"] });
        var netstandard = TestCompiler.Compile("Contoso.Variants", source);
        var package = new TestPackageBuilder("Contoso.Variants", "1.0.0")
            .AddFile("lib/net8.0/Contoso.Variants.dll", net8.Assembly).AddFile("lib/net8.0/Contoso.Variants.xml", net8.Xml)
            .AddFile("lib/netstandard2.0/Contoso.Variants.dll", netstandard.Assembly).AddFile("lib/netstandard2.0/Contoso.Variants.xml", netstandard.Xml);
        await using var site = await SiteBuilder.BuildAsync([package]);
        var document = Load(site, "api/contoso.variants/Contoso.Variants.Widget.html");

        var name = document.GetElementById("Name")!;
        Assert.Equal(["public string? Name { get; set; }", "public string Name { get; set; }"], name.QuerySelectorAll<Element>("pre.declaration").Select(p => p.TextContent));
        Assert.Equal(["net8.0", "netstandard2.0"], name.QuerySelectorAll<Element>(".declaration-variant").Select(v => v.GetAttribute("data-tfms")));
        Assert.Equal(["Documentation for net8.0", "Documentation for netstandard2.0"], name.QuerySelectorAll<Element>(".documentation-variant .variant-label").Select(l => l.TextContent));

        var common = document.GetElementById("Common")!;
        Assert.Single(common.QuerySelectorAll<Element>("pre.declaration"));
        Assert.Null(common.QuerySelector(".member-availability"));
        Assert.Equal("net8.0 netstandard2.0", document.QuerySelector("[data-tfm-options]")!.GetAttribute("data-tfm-options"));
    }

    [Fact]
    public async Task MissingDocumentation_HasIntentionalOutput()
    {
        var assembly = TestCompiler.Compile("Contoso.Bare", "namespace Contoso.Bare { public class Thing { public void Run() { } } }");
        var package = new TestPackageBuilder("Contoso.Bare", "2.0.0").AddFile("lib/net8.0/Contoso.Bare.dll", assembly.Assembly);
        await using var site = await SiteBuilder.BuildAsync([package]);

        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        Assert.False(File.Exists(site.Output / "contoso.bare" / "index.html"));
        var reference = Load(site, "api/contoso.bare/index.html");
        Assert.Equal("/api/contoso.bare/namespaces/Contoso.Bare.html", reference.QuerySelector(".api-table a")!.GetAttribute("href"));

        var type = Load(site, "api/contoso.bare/Contoso.Bare.Thing.html");
        Assert.Equal("No documentation is available.", type.GetElementById("Run")!.QuerySelector(".no-documentation")!.TextContent);
        Assert.DoesNotContain("DG3001", site.Result.AllOutput);
        Assert.Contains("DG3002", site.Result.AllOutput);
    }

    [Fact]
    public async Task SeveralPackages_OneSectionPerPackage()
    {
        var first = ApiModelTests.CreateKitchenSinkPackage("Contoso.First");
        var assembly = TestCompiler.Compile("Contoso.Second", "namespace Contoso.Second { /// <summary>Second.</summary>\npublic class Other { } }");
        var second = new TestPackageBuilder("Contoso.Second", "1.0.0").AddFile("lib/net8.0/Contoso.Second.dll", assembly.Assembly).AddFile("lib/net8.0/Contoso.Second.xml", assembly.Xml);
        await using var site = await SiteBuilder.BuildAsync([first, second]);

        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        var reference = Load(site, "api/contoso.first/index.html");
        Assert.Equal("Description of Contoso.First", reference.QuerySelector("p.lead")!.TextContent);
        Assert.Equal(["Contoso.Library", "Contoso.Library.Collections"], reference.QuerySelectorAll<Element>(".api-table a").Select(a => a.TextContent));
        Assert.Equal(["Contoso.Second"], Load(site, "api/contoso.second/index.html").QuerySelectorAll<Element>(".api-table a").Select(a => a.TextContent));
        var items = reference.QuerySelectorAll<Element>(".sidebar > ul > li");
        var api = items[items.Count - 1];
        Assert.Equal("API reference", api.QuerySelector(".nav-group")!.TextContent);
        Assert.Contains("nav-api-reference", api.GetAttribute("class")!.Split(' '));
        Assert.Equal(["Contoso.First", "Contoso.Second"], api.QuerySelectorAll<Element>("details > ul > li > a").Where(a => a.GetAttribute("href") is "/api/contoso.first/" or "/api/contoso.second/").Select(a => a.TextContent));
    }

    [Fact]
    public async Task SourceLinks_ShowPrecision()
    {
        var assembly = TestCompiler.Compile("Contoso.Sources", [("/_/src/Widget.cs", "namespace Contoso.Sources\n{\n    public class Widget\n    {\n        public int Compute(int x)\n        {\n            return x;\n        }\n    }\n}\n")], new CompileOptions { SourceLink = SourceLinkTests.GitHubSourceLink });
        var package = new TestPackageBuilder("Contoso.Sources", "1.0.0").AddFile("lib/net8.0/Contoso.Sources.dll", assembly.Assembly).AddFile("lib/net8.0/Contoso.Sources.pdb", assembly.Pdb!);
        await using var site = await SiteBuilder.BuildAsync([package]);
        var document = Load(site, "api/contoso.sources/Contoso.Sources.Widget.html");

        var typeLink = document.QuerySelector("article > .heading-row .source-link")!;
        Assert.Equal("file", typeLink.GetAttribute("data-precision"));
        Assert.Equal("file", typeLink.TextContent!.Trim());
        Assert.Equal("Source file on GitHub", typeLink.GetAttribute("aria-label"));
        var methodLink = document.GetElementById("Compute(System.Int32)")!.QuerySelector(".heading-row > .source-links > .source-link")!;
        Assert.Equal("line", methodLink.GetAttribute("data-precision"));
        Assert.Equal("code", methodLink.TextContent!.Trim());
        Assert.Equal("Source code on GitHub", methodLink.GetAttribute("aria-label"));
        Assert.Equal($"https://github.com/contoso/library/blob/{SourceLinkTests.Commit}/src/Widget.cs#L6", methodLink.GetAttribute("href"));
    }

    [Fact]
    public async Task Subpath_LinksIncludeTheBasePath()
    {
        await using var site = await BuildKitchenSinkAsync(arguments: ["--base-path", "/docs/"]);
        var document = Load(site, "api/contoso.library/Contoso.Library.Client.html");

        Assert.All(document.QuerySelectorAll<Element>("a[href^='/']"), a => Assert.StartsWith("/docs/", a.GetAttribute("href")));
        Assert.Equal("/docs/_assets/site.css", document.QuerySelector("link[rel=stylesheet]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task FilteredApis_DoNotProduceBrokenPages()
    {
        await using var site = await BuildKitchenSinkAsync(extraConfiguration: """ "api": { "exclude": [ "N:Contoso.Library.Collections", "T:Contoso.Library.Message" ] }, """);

        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        Assert.False(File.Exists(site.Output / "api" / "contoso.library" / "namespaces" / "Contoso.Library.Collections.html"));
        Assert.False(File.Exists(site.Output / "api" / "contoso.library" / "Contoso.Library.Message.html"));

        // References to the excluded type are displayed without link
        var client = Load(site, "api/contoso.library/Contoso.Library.Client.html");
        Assert.DoesNotContain(client.QuerySelectorAll<Element>("a"), a => a.GetAttribute("href")?.Contains("Contoso.Library.Message.html", StringComparison.Ordinal) == true);
    }

    private static Task<SiteBuilder> BuildKitchenSinkAsync(string extraConfiguration = "", string[]? arguments = null)
    {
        return SiteBuilder.BuildAsync([ApiModelTests.CreateKitchenSinkPackage()], extraConfiguration: extraConfiguration, arguments: arguments);
    }

    private static Document Load(SiteBuilder site, string path) => HtmlDocumentLoader.Parse(site.ReadFile(path));
}
