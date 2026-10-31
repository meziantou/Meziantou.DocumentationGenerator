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
            "api/packages/contoso.library.html",
            "api/namespaces/Contoso.Library.html",
            "api/namespaces/Contoso.Library.Collections.html",
            "api/Contoso.Library.Client.html",
            "api/Contoso.Library.MessageKind.html",
            "api/Contoso.Library.MessageHandler.html",
            "api/Contoso.Library.IChannel-1.html",
            "api/Contoso.Library.Point.html",
            "api/Contoso.Library.Collections.Tree-1.Node.Child-1.html",
        ];
        Assert.All(expected, file => Assert.True(File.Exists(site.Output / file), file));
        Assert.False(File.Exists(site.Output / "contoso.library" / "index.html"));
    }

    [Fact]
    public async Task TypePage_StructureAndMembers()
    {
        await using var site = await BuildKitchenSinkAsync();
        var document = Load(site, "api/Contoso.Library.Client.html");

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
        Assert.Equal(["Contoso.Library.Message.html", "https://learn.microsoft.com/dotnet/api/system.int32"], declaration.QuerySelectorAll<Element>("a").Select(a => a.GetAttribute("href")));
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

        var enumPage = Load(site, "api/Contoso.Library.MessageKind.html");
        Assert.Equal("MessageKind Enum", enumPage.QuerySelector("h1")!.TextContent);
        Assert.Equal(["None", "Text", "Binary"], enumPage.QuerySelectorAll<Element>("table.api-table tbody tr td:first-child").Select(c => c.TextContent));
        Assert.Equal("2", enumPage.GetElementById("Binary")!.QuerySelectorAll<Element>("td")[1].TextContent);

        var delegatePage = Load(site, "api/Contoso.Library.MessageHandler.html");
        Assert.Equal("MessageHandler Delegate", delegatePage.QuerySelector("h1")!.TextContent);
        Assert.Contains("The message.", delegatePage.QuerySelector("dl.parameters")!.TextContent);

        var sealedPage = Load(site, "api/Contoso.Library.Sealed.html");
        Assert.Equal("Contoso.Library.Base.html", sealedPage.QuerySelector("dl.api-relations a")!.GetAttribute("href"));
        Assert.Null(sealedPage.GetElementById("Protected"));

        var legacy = Load(site, "api/Contoso.Library.Legacy.html");
        Assert.Equal("Obsolete: Use Client instead.", legacy.QuerySelector(".badge-obsolete")!.TextContent);

        var nested = Load(site, "api/Contoso.Library.Collections.Tree-1.Node.Child-1.html");
        Assert.Equal("Tree<T>.Node.Child<TKey> Class", nested.QuerySelector("h1")!.TextContent);
        Assert.Equal("Contoso.Library.Collections.Tree-1.Node.html", nested.QuerySelectorAll<Element>("dl.api-metadata a")[^1].GetAttribute("href"));
    }

    [Fact]
    public async Task NamespaceAndReferencePages()
    {
        await using var site = await BuildKitchenSinkAsync();

        var ns = Load(site, "api/namespaces/Contoso.Library.html");
        Assert.Equal(["Classes", "Structs", "Interfaces", "Enums", "Delegates"], ns.QuerySelectorAll<Element>("h2:not(#page-toc-title)").Select(h => h.TextContent));
        var modernOnly = ns.QuerySelectorAll<Element>("tr").Single(r => r.TextContent!.Contains("ModernOnly", StringComparison.Ordinal));
        Assert.Equal("net8.0", modernOnly.GetAttribute("data-tfms"));

        var reference = Load(site, "api/packages/contoso.library.html");
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
        var document = Load(site, "api/Contoso.Variants.Widget.html");

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
        var reference = Load(site, "api/packages/contoso.bare.html");
        Assert.Equal("../namespaces/Contoso.Bare.html", reference.QuerySelector(".api-table a")!.GetAttribute("href"));

        var type = Load(site, "api/Contoso.Bare.Thing.html");
        Assert.Equal("No documentation is available.", type.GetElementById("Run")!.QuerySelector(".no-documentation")!.TextContent);
        Assert.DoesNotContain("DG3001", site.Result.AllOutput);
        Assert.Contains("DG3002", site.Result.AllOutput);
    }

    [Fact]
    public async Task SeveralPackages_ShareTheApiReference()
    {
        var first = ApiModelTests.CreateKitchenSinkPackage("Contoso.First");
        var assembly = TestCompiler.Compile("Contoso.Second", "namespace Contoso.Second { /// <summary>Second.</summary>\npublic class Other { } }");
        var second = new TestPackageBuilder("Contoso.Second", "1.0.0").AddFile("lib/net8.0/Contoso.Second.dll", assembly.Assembly).AddFile("lib/net8.0/Contoso.Second.xml", assembly.Xml);
        await using var site = await SiteBuilder.BuildAsync([first, second]);

        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);

        // The API reference lists the namespaces of every package, then the packages
        var home = Load(site, "api/index.html");
        Assert.Equal("API reference", home.QuerySelector("h1")!.TextContent);
        var tables = home.QuerySelectorAll<Element>(".api-table");
        Assert.Equal(["Contoso.Library", "Contoso.Library.Collections", "Contoso.Second"], tables[0].QuerySelectorAll<Element>("a").Select(a => a.TextContent));
        Assert.Equal(["namespaces/Contoso.Library.html", "namespaces/Contoso.Library.Collections.html", "namespaces/Contoso.Second.html"], tables[0].QuerySelectorAll<Element>("a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["Contoso.First", "Contoso.Second"], tables[1].QuerySelectorAll<Element>("a").Select(a => a.TextContent));
        Assert.Equal(["packages/contoso.first.html", "packages/contoso.second.html"], tables[1].QuerySelectorAll<Element>("a").Select(a => a.GetAttribute("href")));

        // Each package has a page listing its own namespaces
        var reference = Load(site, "api/packages/contoso.first.html");
        Assert.Equal("Contoso.First package", reference.QuerySelector("h1")!.TextContent);
        Assert.Equal("Description of Contoso.First", reference.QuerySelector("p.lead")!.TextContent);
        Assert.StartsWith("https://www.nuget.org/packages/Contoso.First/", reference.QuerySelector(".api-metadata a.nuget-link")!.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Equal(["Contoso.Library", "Contoso.Library.Collections"], reference.QuerySelectorAll<Element>(".api-table a").Select(a => a.TextContent));
        Assert.Equal(["Contoso.Second"], Load(site, "api/packages/contoso.second.html").QuerySelectorAll<Element>(".api-table a").Select(a => a.TextContent));

        // The sidebar lists namespaces directly under the API reference
        var items = reference.QuerySelectorAll<Element>(".sidebar > ul > li");
        var api = items[items.Count - 1];
        var apiLink = api.QuerySelector(":scope > a")!;
        Assert.Equal("API reference", apiLink.TextContent);
        Assert.Equal("../", apiLink.GetAttribute("href"));
        Assert.Contains("nav-api-reference", api.GetAttribute("class")!.Split(' '));
        Assert.Equal(["Contoso.Library", "Contoso.Library.Collections", "Contoso.Second"], api.QuerySelectorAll<Element>(":scope > details > ul > li > a").Select(a => a.TextContent));

        // A type page links to its package; a namespace of a single package has no package column
        var other = Load(site, "api/Contoso.Second.Other.html");
        Assert.Equal("packages/contoso.second.html", other.QuerySelector(".api-metadata a[href^='packages/']")!.GetAttribute("href"));
        Assert.Equal("https://www.nuget.org/packages/Contoso.Second/1.0.0", other.QuerySelector(".api-metadata a.nuget-link")!.GetAttribute("href"));
        Assert.Equal("1.0.0", other.QuerySelector(".api-metadata a.nuget-link")!.TextContent);
        Assert.Equal(["Name", "Description"], Load(site, "api/namespaces/Contoso.Second.html").QuerySelectorAll<Element>(".api-table th").Select(th => th.TextContent));
    }

    [Fact]
    public async Task NamespaceOfSeveralPackages_ShowsThePackageOfEachType()
    {
        var first = new TestPackageBuilder("Contoso.Guard", "1.0.0").AddFile("lib/net8.0/Contoso.Guard.dll", LinkServiceTests.Guard.Value.Assembly);
        var second = new TestPackageBuilder("Contoso.OtherGuard", "1.0.0").AddFile("lib/net8.0/Contoso.OtherGuard.dll", LinkServiceTests.OtherGuard.Value.Assembly);
        await using var site = await SiteBuilder.BuildAsync([first, second]);

        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        var ns = Load(site, "api/namespaces/Utils.html");
        Assert.Equal(["Name", "Package", "Description"], ns.QuerySelectorAll<Element>(".api-table th").Select(th => th.TextContent));
        var rows = ns.QuerySelectorAll<Element>(".api-table tbody tr");
        Assert.Equal(["../Utils.Guard--Contoso.Guard.html", "../Utils.Guard--Contoso.OtherGuard.html"], rows.Select(r => r.QuerySelector("td a")!.GetAttribute("href")));
        Assert.Equal(["Contoso.Guard", "Contoso.OtherGuard"], rows.Select(r => r.QuerySelectorAll<Element>("td")[1].TextContent));
        Assert.Equal("../packages/contoso.otherguard.html", rows[1].QuerySelectorAll<Element>("td")[1].QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task SourceLinks_ShowPrecision()
    {
        var assembly = TestCompiler.Compile("Contoso.Sources", [("/_/src/Widget.cs", "namespace Contoso.Sources\n{\n    public class Widget\n    {\n        public int Compute(int x)\n        {\n            return x;\n        }\n    }\n}\n")], new CompileOptions { SourceLink = SourceLinkTests.GitHubSourceLink });
        var package = new TestPackageBuilder("Contoso.Sources", "1.0.0").AddFile("lib/net8.0/Contoso.Sources.dll", assembly.Assembly).AddFile("lib/net8.0/Contoso.Sources.pdb", assembly.Pdb!);
        await using var site = await SiteBuilder.BuildAsync([package]);
        var document = Load(site, "api/Contoso.Sources.Widget.html");

        // The type's source is listed after its assembly and package
        Assert.Null(document.QuerySelector("article > .heading-row .source-link"));
        Assert.Equal(["Namespace", "Assembly", "Package", "Source", "Available in package assets"], document.QuerySelectorAll<Element>("dl.api-metadata > dt").Select(dt => dt.TextContent));
        var typeLink = document.QuerySelector("dl.api-metadata .source-link")!;
        Assert.Equal("file", typeLink.GetAttribute("data-precision"));
        Assert.Equal("Widget.cs", typeLink.TextContent!.Trim());
        Assert.Equal("Widget.cs, source file on GitHub", typeLink.GetAttribute("aria-label"));
        var methodLink = document.GetElementById("Compute(System.Int32)")!.QuerySelector(".heading-row > .source-links > .source-link")!;
        Assert.Equal("line", methodLink.GetAttribute("data-precision"));
        Assert.Equal("Widget.cs", methodLink.TextContent!.Trim());
        Assert.Equal("Widget.cs, source code on GitHub", methodLink.GetAttribute("aria-label"));
        Assert.Equal($"https://github.com/contoso/library/blob/{SourceLinkTests.Commit}/src/Widget.cs#L6", methodLink.GetAttribute("href"));
    }

    [Fact]
    public async Task Subpath_LinksAreRelativeToThePage()
    {
        await using var site = await BuildKitchenSinkAsync(arguments: ["--base-path", "/docs/"]);
        var document = Load(site, "api/Contoso.Library.Client.html");

        Assert.Empty(document.QuerySelectorAll<Element>("[href^='/'], [src^='/']"));
        Assert.Equal("../_assets/site.css", document.QuerySelector("link[rel=stylesheet]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task FilteredApis_DoNotProduceBrokenPages()
    {
        await using var site = await BuildKitchenSinkAsync(extraConfiguration: """ "api": { "exclude": [ "N:Contoso.Library.Collections", "T:Contoso.Library.Message" ] }, """);

        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);
        Assert.False(File.Exists(site.Output / "api" / "namespaces" / "Contoso.Library.Collections.html"));
        Assert.False(File.Exists(site.Output / "api" / "Contoso.Library.Message.html"));

        // References to the excluded type are displayed without link
        var client = Load(site, "api/Contoso.Library.Client.html");
        Assert.DoesNotContain(client.QuerySelectorAll<Element>("a"), a => a.GetAttribute("href")?.Contains("Contoso.Library.Message.html", StringComparison.Ordinal) == true);
    }

    private static Task<SiteBuilder> BuildKitchenSinkAsync(string extraConfiguration = "", string[]? arguments = null)
    {
        return SiteBuilder.BuildAsync([ApiModelTests.CreateKitchenSinkPackage()], extraConfiguration: extraConfiguration, arguments: arguments);
    }

    private static Document Load(SiteBuilder site, string path) => HtmlDocumentLoader.Parse(site.ReadFile(path));
}
