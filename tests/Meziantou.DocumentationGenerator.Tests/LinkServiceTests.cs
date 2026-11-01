using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class LinkServiceTests
{
    internal static readonly Lazy<CompiledAssembly> Guard = new(() => TestCompiler.Compile("Contoso.Guard", """
        namespace Utils
        {
            /// <summary>Guard.</summary>
            public static class Guard
            {
                /// <summary>Checks.</summary>
                public static void NotNull(object value) { }
            }
        }
        """));

    internal static readonly Lazy<CompiledAssembly> OtherGuard = new(() => TestCompiler.Compile("Contoso.OtherGuard", """
        namespace Utils
        {
            /// <summary>Another guard.</summary>
            public static class Guard
            {
            }
        }
        """));

    private static readonly Lazy<CompiledAssembly> CaseOnly = new(() => TestCompiler.Compile("Contoso.Case", """
        namespace Contoso
        {
            /// <summary>Upper.</summary>
            public class Item { }

            /// <summary>Lower.</summary>
            public class item { }

            /// <summary>Reserved name.</summary>
            public class CON { }
        }
        """));

    [Theory]
    [InlineData("T:Contoso.Library.Client", "/api/Contoso.Library.Client.html")]
    [InlineData("T:Contoso.Library.Collections.Tree`1", "/api/Contoso.Library.Collections.Tree-1.html")]
    [InlineData("T:Contoso.Library.Collections.Tree`1.Node", "/api/Contoso.Library.Collections.Tree-1.Node.html")]
    [InlineData("T:Contoso.Library.Collections.Tree`1.Node.Child`1", "/api/Contoso.Library.Collections.Tree-1.Node.Child-1.html")]
    [InlineData("M:Contoso.Library.Client.Send(System.String)", "/api/Contoso.Library.Client.html#Send(System.String)")]
    [InlineData("M:Contoso.Library.Client.#ctor(System.String)", "/api/Contoso.Library.Client.html#%23ctor(System.String)")]
    [InlineData("M:Contoso.Library.Client.#ctor", "/api/Contoso.Library.Client.html#%23ctor")]
    [InlineData("M:Contoso.Library.Client.Convert``1(System.Object)", "/api/Contoso.Library.Client.html#Convert%60%601(System.Object)")]
    [InlineData("M:Contoso.Library.Client.op_Implicit(System.String)~Contoso.Library.Client", "/api/Contoso.Library.Client.html#op_Implicit(System.String)~Contoso.Library.Client")]
    [InlineData("M:Contoso.Library.Client.op_Explicit(Contoso.Library.Client)~System.String", "/api/Contoso.Library.Client.html#op_Explicit(Contoso.Library.Client)~System.String")]
    [InlineData("M:Contoso.Library.Client.System#IDisposable#Dispose", "/api/Contoso.Library.Client.html#System%23IDisposable%23Dispose")]
    [InlineData("P:Contoso.Library.Client.Item(System.Int32)", "/api/Contoso.Library.Client.html#Item(System.Int32)")]
    [InlineData("M:Contoso.Library.Collections.Tree`1.Map``1(System.Func{`0,``0})", "/api/Contoso.Library.Collections.Tree-1.html#Map%60%601(System.Func%7B%600,%60%600%7D)")]
    public async Task Routes_AreDeterministic(string documentationId, string expectedUrl)
    {
        await using var build = await BuildAsync([ApiModelTests.CreateKitchenSinkPackage()]);
        var symbol = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId(documentationId));

        Assert.Equal(expectedUrl, build.Context.Links!.GetUrl(symbol));
    }

    [Fact]
    public async Task MemberAnchors_AreTheMemberPartOfTheDocumentationId()
    {
        await using var build = await BuildAsync([ApiModelTests.CreateKitchenSinkPackage()]);
        var send = (ApiMemberModel)Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("M:Contoso.Library.Client.Send(Contoso.Library.Message,System.Int32)"));

        Assert.Equal("Send(Contoso.Library.Message,System.Int32)", RouteNames.GetMemberAnchor(send));
        Assert.Equal("Send-overloads", RouteNames.GetOverloadAnchor(send.OverloadName));
        Assert.Equal("/api/Contoso.Library.Client.html#Send-overloads", build.Context.Links!.GetOverloadGroupUrl(send));
    }

    [Fact]
    public async Task FixedRoutes_RootAndSubpath()
    {
        await using var root = await BuildAsync([ApiModelTests.CreateKitchenSinkPackage()]);
        await using var docs = await BuildAsync([ApiModelTests.CreateKitchenSinkPackage()], """ "site": { "basePath": "/docs/" }, """);

        Assert.Equal("/", root.Context.Links!.HomeUrl);
        Assert.Equal("/docs/", docs.Context.Links!.HomeUrl);
        Assert.Equal("/404.html", root.Context.Links.NotFoundUrl);
        Assert.Equal("/docs/404.html", docs.Context.Links.NotFoundUrl);
        Assert.Equal("/docs/api/packages/contoso.library.html", docs.Context.Links.GetPackageUrl("Contoso.Library"));
        Assert.Equal("/docs/api/namespaces/Contoso.Library.Collections.html", docs.Context.Links.GetNamespaceUrl("Contoso.Library.Collections"));
        Assert.Equal("/docs/api/namespaces/_global.html", docs.Context.Links.GetNamespaceUrl(""));
        Assert.Equal("/docs/_assets/site.css", docs.Context.Links.GetAssetUrl("site.css"));
        Assert.Equal(
            root.Context.Links.Routes.Select(r => r.FilePath),
            docs.Context.Links.Routes.Select(r => r.FilePath));
    }

    [Fact]
    public void UrlBuilder_EncodesPathsAndFragments()
    {
        var urls = new UrlBuilder(UrlBasePath.Parse("/docs/"));

        Assert.Equal("/docs/guides/", urls.GetUrl("guides/index.html"));
        Assert.Equal("/docs/a%20b/%C3%A9t%C3%A9.html", urls.GetUrl("a b/été.html"));
        Assert.Equal("/docs/page.html?q=1#section-1", urls.GetUrl("page.html", "section-1", "q=1"));
        Assert.Equal("/docs/page.html#a%25b%23c", urls.GetUrl("page.html", "a%b#c"));
    }

    [Theory]
    [InlineData("index.html", "/docs/", "./")]
    [InlineData("index.html", "/docs/guide.html", "guide.html")]
    [InlineData("guides/index.html", "/docs/guides/", "./")]
    [InlineData("guides/install.html", "/docs/guides/install.html", "install.html")]
    [InlineData("guides/install.html", "/docs/guides/start.html?q=1&x=2#install", "start.html?q=1&x=2#install")]
    [InlineData("guides/install.html", "/docs/guide.html", "../guide.html")]
    [InlineData("api/namespaces/N.html", "/docs/_assets/site.css", "../../_assets/site.css")]
    [InlineData("api/Type.html", "/docs/api/Other.html#M%60%601", "Other.html#M%60%601")]
    [InlineData("api/Type.html", "/docs/api/packages/other.html", "packages/other.html")]
    [InlineData("guides/install.html", "/docs/", "../")]
    [InlineData("a b/été.html", "/docs/a%20b/autre.html", "autre.html")]
    [InlineData("guide.html", "/docs/a%20b/%C3%A9t%C3%A9.html#x%25y", "a%20b/%C3%A9t%C3%A9.html#x%25y")]
    [InlineData("guide.html", "#main", "#main")]
    [InlineData("guide.html", "https://example.com/docs/", "https://example.com/docs/")]
    [InlineData("guide.html", "//cdn.example.com/image.png", "//cdn.example.com/image.png")]
    [InlineData("guide.html", "mailto:docs@example.com", "mailto:docs@example.com")]
    [InlineData("guide.html", "/outside/page.html", "/outside/page.html")]
    public void UrlBuilder_MakesSiteUrlsRelativeToTheOutputFile(string filePath, string url, string expected)
    {
        var urls = new UrlBuilder(UrlBasePath.Parse("/docs/"));
        Assert.Equal(expected, urls.GetRelativeUrl(filePath, url));
    }

    [Fact]
    public void LinkValidator_ResolvesFileRelativeUrls()
    {
        var diagnostics = new DiagnosticBag();
        var validator = new SiteLinkValidator(new UrlBuilder(UrlBasePath.Parse("/docs/")), diagnostics);
        validator.AddFile("index.html", [], ["guides/", "guides/install.html?q=1#install"]);
        validator.AddFile("guides/index.html", [], ["../", "./", "install.html#install"]);
        validator.AddFile("guides/install.html", ["install"], ["../", "?tab=cli#install", "../_assets/site.css"]);
        validator.AddFile("_assets/site.css", [], []);
        validator.Validate();
        Assert.Empty(diagnostics.GetDiagnostics());
    }

    [Fact]
    public async Task SameTypeInTwoAssembliesOfAPackage_IsDisambiguated()
    {
        var package = new TestPackageBuilder("Contoso.Guards", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.Guard.dll", Guard.Value.Assembly).AddFile("lib/net8.0/Contoso.OtherGuard.dll", OtherGuard.Value.Assembly);
        await using var build = await BuildAsync([package]);

        var urls = build.Context.ApiCatalog.FindByDocumentationId("T:Utils.Guard").Select(build.Context.Links!.GetUrl).ToList();
        Assert.Equal(["/api/Utils.Guard--Contoso.Guard.html", "/api/Utils.Guard--Contoso.OtherGuard.html"], urls);
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4004");
    }

    [Fact]
    public async Task SameTypeInTwoPackages_IsDisambiguated()
    {
        var first = new TestPackageBuilder("Contoso.Guard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.Guard.dll", Guard.Value.Assembly);
        var second = new TestPackageBuilder("Contoso.OtherGuard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.OtherGuard.dll", OtherGuard.Value.Assembly);
        await using var build = await BuildAsync([first, second]);
        var links = build.Context.Links!;

        // Type pages do not depend on the package, so the pages of both types are disambiguated with the assembly name
        Assert.Equal(["/api/Utils.Guard--Contoso.Guard.html", "/api/Utils.Guard--Contoso.OtherGuard.html"], build.Context.ApiCatalog.FindByDocumentationId("T:Utils.Guard").Select(links.GetUrl));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4004");

        // References are resolved in the package of the page first, and are ambiguous outside of any package
        Assert.Equal("/api/Utils.Guard--Contoso.Guard.html", links.Resolve("T:Utils.Guard", new LinkContext(null, "Contoso.Guard")).Url);
        Assert.Equal("/api/Utils.Guard--Contoso.OtherGuard.html", links.Resolve("T:Utils.Guard", new LinkContext("Contoso.OtherGuard/Contoso.OtherGuard", null)).Url);
        var ambiguous = links.Resolve("T:Utils.Guard", LinkContext.None);
        Assert.Equal(CrefResolutionKind.Ambiguous, ambiguous.Kind);
        Assert.Equal(["Contoso.Guard!T:Utils.Guard", "Contoso.OtherGuard!T:Utils.Guard"], ambiguous.Candidates);
        Assert.Equal("/api/Utils.Guard--Contoso.OtherGuard.html", links.Resolve("Contoso.OtherGuard!T:Utils.Guard", LinkContext.None).Url);
    }

    [Theory]
    [InlineData("Utils")]
    [InlineData("N:Utils")]
    public async Task NamespaceInTwoPackages_HasASinglePage(string cref)
    {
        var first = new TestPackageBuilder("Contoso.Guard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.Guard.dll", Guard.Value.Assembly);
        var second = new TestPackageBuilder("Contoso.OtherGuard", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.OtherGuard.dll", OtherGuard.Value.Assembly);
        await using var build = await BuildAsync([first, second]);
        var links = build.Context.Links!;

        Assert.Equal(["Utils"], build.Context.ApiCatalog.Namespaces.Select(ns => ns.Name));
        Assert.HasCount(2, build.Context.ApiCatalog.Namespaces[0].Types);

        var resolution = links.Resolve(cref, LinkContext.None);
        Assert.Equal(CrefResolutionKind.Resolved, resolution.Kind);
        Assert.Equal("/api/namespaces/Utils.html", resolution.Url);
        Assert.Equal("/api/namespaces/Utils.html", links.Resolve(cref, new LinkContext(null, "Contoso.Guard")).Url);

        // A package qualifier is accepted when the package contributes to the namespace
        Assert.Equal("/api/namespaces/Utils.html", links.Resolve("Contoso.OtherGuard!" + cref, LinkContext.None).Url);
        Assert.Equal("/api/namespaces/Utils.html", links.Resolve("contoso.otherguard!" + cref, LinkContext.None).Url);
        Assert.Equal(CrefResolutionKind.Unresolved, links.Resolve("Contoso.Missing!" + cref, LinkContext.None).Kind);
    }

    [Fact]
    public async Task CaseOnlyCollisionsAndReservedNames()
    {
        var package = new TestPackageBuilder("Contoso.Case", "1.0.0").AddFile("README.md", "x").AddFile("lib/net8.0/Contoso.Case.dll", CaseOnly.Value.Assembly);
        await using var build = await BuildAsync([package]);
        var links = build.Context.Links!;
        var item = links.GetUrl(Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("T:Contoso.Item")))!;
        var lowerItem = links.GetUrl(Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("T:Contoso.item")))!;

        Assert.NotEqual(item, lowerItem, StringComparer.OrdinalIgnoreCase);
        Assert.Matches("^/api/Contoso.Item--[0-9a-f]{8}.html$", item);
        Assert.Equal("/api/Contoso.CON.html", links.GetUrl(Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("T:Contoso.CON"))));
        Assert.Equal("_CON", RouteNames.SanitizeFileName("CON"));
        Assert.Equal("_nul.txt", RouteNames.SanitizeFileName("nul.txt"));
    }

    [Fact]
    public async Task GlobalTypeNamedIndex_DoesNotReplaceTheApiReference()
    {
        var assembly = TestCompiler.Compile("Contoso.Index", "public class index { }");
        var package = new TestPackageBuilder("Contoso.Index", "1.0.0").AddFile("lib/net8.0/Contoso.Index.dll", assembly.Assembly);
        await using var build = await BuildAsync([package]);
        var links = build.Context.Links!;

        Assert.Equal("/api/index--Contoso.Index.html", links.GetUrl(Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("T:index"))));
        Assert.True(links.TryGetRoute("api/index.html", out var route));
        Assert.Equal(PageKind.ApiReference, route.Kind);
        Assert.DoesNotContain(build.Diagnostics, d => d.Code is "DG4004" or "DG4005");
    }

    [Fact]
    public async Task Urls_DoNotDependOnDiscoveryOrder()
    {
        var first = ApiModelTests.CreateKitchenSinkPackage("Contoso.First");
        var second = ApiModelTests.CreateKitchenSinkPackage("Contoso.Second");
        await using var forward = await BuildAsync([first, second]);
        await using var reversed = await BuildAsync([second, first]);

        Assert.Equal(forward.Context.Links!.Routes.Select(r => r.FilePath), reversed.Context.Links!.Routes.Select(r => r.FilePath));
    }

    [Theory]
    [InlineData("T:Contoso.Library.Client", "Resolved", "/api/Contoso.Library.Client.html", "Client")]
    [InlineData("Contoso.Library.Client", "Resolved", "/api/Contoso.Library.Client.html", "Client")]
    [InlineData("Contoso.Library.Collections.Tree<T>.Node", "Resolved", "/api/Contoso.Library.Collections.Tree-1.Node.html", "Tree<T>.Node")]
    [InlineData("Contoso.Library.Collections.Tree{T}", "Resolved", "/api/Contoso.Library.Collections.Tree-1.html", "Tree<T>")]
    [InlineData("M:Contoso.Library.Client.Send(System.String)", "Resolved", "/api/Contoso.Library.Client.html#Send(System.String)", "Client.Send")]
    [InlineData("M:Contoso.Library.Client.Send", "Resolved", "/api/Contoso.Library.Client.html#Send-overloads", "Client.Send")]
    [InlineData("Contoso.Library.Client.Name", "Resolved", "/api/Contoso.Library.Client.html#Name", "Client.Name")]
    [InlineData("M:Contoso.Library.Client.Convert``1(System.Object)", "Resolved", "/api/Contoso.Library.Client.html#Convert%60%601(System.Object)", "Client.Convert")]
    [InlineData("N:Contoso.Library.Collections", "Resolved", "/api/namespaces/Contoso.Library.Collections.html", "Contoso.Library.Collections")]
    [InlineData("Contoso.Library.Collections", "Resolved", "/api/namespaces/Contoso.Library.Collections.html", "Contoso.Library.Collections")]
    [InlineData("T:System.String", "External", "https://learn.microsoft.com/dotnet/api/system.string", "String")]
    [InlineData("T:System.Collections.Generic.List`1", "External", "https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1", "List")]
    [InlineData("M:System.String.Join(System.String,System.String[])", "External", "https://learn.microsoft.com/dotnet/api/system.string.join", "String.Join")]
    [InlineData("F:System.String.Empty", "External", "https://learn.microsoft.com/dotnet/api/system.string.empty", "String.Empty")]
    [InlineData("F:System.StringComparison.Ordinal", "External", "https://learn.microsoft.com/dotnet/api/system.stringcomparison#system-stringcomparison-ordinal", "StringComparison.Ordinal")]
    [InlineData("System.StringComparison.Ordinal", "External", "https://learn.microsoft.com/dotnet/api/system.stringcomparison#system-stringcomparison-ordinal", "StringComparison.Ordinal")]
    [InlineData("F:System.Environment.SpecialFolder.Desktop","External", "https://learn.microsoft.com/dotnet/api/system.environment.specialfolder#system-environment-specialfolder-desktop", "SpecialFolder.Desktop")]
    [InlineData("M:System.Linq.Enumerable.Select{TSource,TResult}", "External", "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.select", "Enumerable.Select")]
    [InlineData("M:System.String.System#IComparable#CompareTo(System.Object)", "External", "https://learn.microsoft.com/dotnet/api/system.string.system-icomparable-compareto", "String.System#IComparable#CompareTo")]
    [InlineData("M:System.Int32.System#Numerics#IAdditionOperators{System#Int32,System#Int32,System#Int32}#op_Addition(System.Int32,System.Int32)", "External", "https://learn.microsoft.com/dotnet/api/system.int32.system-numerics-iadditionoperators-system-int32-system-int32-system-int32--op_addition", "Int32.System#Numerics#IAdditionOperators{System#Int32,System#Int32,System#Int32}#op_Addition")]
    [InlineData("T:Newtonsoft.Json.JsonConvert", "Unresolved", null, "JsonConvert")]
    [InlineData("!:MissingType", "Unresolved", null, "MissingType")]
    [InlineData("Contoso.Library.DoesNotExist", "Unresolved", null, "DoesNotExist")]
    public async Task CrefResolution(string cref, string expectedKind, string? expectedUrl, string expectedDisplayName)
    {
        await using var build = await BuildAsync([ApiModelTests.CreateKitchenSinkPackage()], """ "xrefs": [ { "namespace": "System", "url": "https://learn.microsoft.com/dotnet/api/{uid}" } ], """);
        var resolution = build.Context.Links!.Resolve(cref, new LinkContext("Contoso.Library/Contoso.Library", "contoso.library"));

        Assert.Equal(Enum.Parse<CrefResolutionKind>(expectedKind), resolution.Kind);
        Assert.Equal(expectedUrl, resolution.Url);
        Assert.Equal(expectedDisplayName, resolution.DisplayName);
    }

    [Fact]
    public async Task SignatureTypeReferences_PreferTheReferencedAssembly()
    {
        await using var build = await BuildAsync([ApiModelTests.CreateKitchenSinkPackage()]);
        var send = Assert.Single(build.Context.ApiCatalog.FindByDocumentationId("M:Contoso.Library.Client.Send(Contoso.Library.Message,System.Int32)"));
        var declaration = DeclarationFormatter.Format(send.PrimarySymbol);
        var message = declaration.Segments.Single(s => s.Text == "Message");

        var resolution = build.Context.Links!.ResolveType((Meziantou.Framework.PublicApiGenerator.PublicApiNamedTypeReference)message.TypeReference!, LinkContext.None);
        Assert.Equal("/api/Contoso.Library.Message.html", resolution.Url);
    }

    [Fact]
    public async Task UnsafeAndCollidingPaths_AreRejectedBeforeWriting()
    {
        await using var build = await BuildAsync([ApiModelTests.CreateKitchenSinkPackage()]);
        var links = build.Context.Links!;

        Assert.False(links.TryRegister(new SiteRoute("../outside.html", PageKind.Article, "article")));
        Assert.False(links.TryRegister(new SiteRoute("/rooted.html", PageKind.Article, "article")));
        Assert.False(links.TryRegister(new SiteRoute("a\\b.html", PageKind.Article, "article")));
        Assert.False(links.TryRegister(new SiteRoute("API/CONTOSO.LIBRARY.CLIENT.html", PageKind.Article, "article")));
        Assert.True(links.TryRegister(new SiteRoute("guides/intro.html", PageKind.Article, "article")));
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4007");
        Assert.Contains(build.Diagnostics, d => d.Code == "DG4005" && d.Message.Contains("case-insensitively", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalUids()
    {
        Assert.Equal("system.collections.generic.list-1", LinkService.GetExternalUid("T:System.Collections.Generic.List`1"));
        Assert.Equal("system.linq.enumerable.select", LinkService.GetExternalUid("M:System.Linq.Enumerable.Select``2(System.Collections.Generic.IEnumerable{``0},System.Func{``0,``1})"));
        Assert.Equal("system.string.-ctor", LinkService.GetExternalUid("M:System.String.#ctor(System.Char[])"));
        Assert.Equal("system.string", LinkService.GetExternalUid("T:System.String"));
        Assert.Equal("system.collections.generic.dictionary-2.keycollection", LinkService.GetExternalUid("T:System.Collections.Generic.Dictionary`2.KeyCollection"));
        Assert.Equal("system.linq.enumerable.select", LinkService.GetExternalUid("M:System.Linq.Enumerable.Select`2"));
        Assert.Equal("system.string.system-icomparable-compareto", LinkService.GetExternalUid("M:System.String.System#IComparable#CompareTo(System.Object)"));
        Assert.Equal("system.collections.generic.list-1.system-collections-generic-ienumerable-t--getenumerator", LinkService.GetExternalUid("M:System.Collections.Generic.List`1.System#Collections#Generic#IEnumerable{T}#GetEnumerator"));
        Assert.Equal("system.int32.system-numerics-iadditionoperators-system-int32-system-int32-system-int32--op_addition", LinkService.GetExternalUid("M:System.Int32.System#Numerics#IAdditionOperators{System#Int32,System#Int32,System#Int32}#op_Addition(System.Int32,System.Int32)"));
    }

    [Fact]
    public void LinkValidator_ReportsSharedLinksForEachPage()
    {
        var diagnostics = new DiagnosticBag();
        var validator = new SiteLinkValidator(new UrlBuilder(UrlBasePath.Parse("/docs/")), diagnostics);
        validator.AddFile("guides/a.html", ["a"], ["missing.html", "?q=1#a", "#a"]);
        validator.AddFile("guides/b.html", ["b"], ["missing.html", "?q=1#a", "#a"]);
        validator.Validate();

        var messages = diagnostics.GetDiagnostics().Select(d => d.Message).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            [
                "Broken link '#a' in 'guides/b.html': 'guides/b.html' has no element with the identifier 'a'",
                "Broken link '?q=1#a' in 'guides/b.html': 'guides/b.html' has no element with the identifier 'a'",
                "Broken link 'missing.html' in 'guides/a.html': 'guides/missing.html' is not generated",
                "Broken link 'missing.html' in 'guides/b.html': 'guides/missing.html' is not generated",
            ], messages);
    }

    [Fact]
    public void LinkValidator()
    {
        var diagnostics = new DiagnosticBag();
        var validator = new SiteLinkValidator(new UrlBuilder(UrlBasePath.Parse("/docs/")), diagnostics);
        validator.AddFile("index.html", ["top"], ["/docs/a.html#x", "/docs/a.html#missing", "/docs/missing.html", "/other/a.html", "#top", "#nope", "https://example.com", "javascript:alert(1)", "/docs/sub/"]);
        validator.AddFile("a.html", ["x", "x"], ["/docs/"]);
        validator.AddFile("sub/index.html", [], []);
        validator.Validate();

        var messages = diagnostics.GetDiagnostics().Select(d => d.Code + " " + d.Message).ToList();
        Assert.Contains("DG4006 The identifier 'x' is used more than once in 'a.html'", messages);
        Assert.Contains(messages, m => m.Contains("'/docs/a.html#missing'", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("'/docs/missing.html'", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("'/other/a.html'", StringComparison.Ordinal) && m.Contains("outside the base path", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("'#nope'", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("'javascript:alert(1)'", StringComparison.Ordinal));
        Assert.HasCount(6, messages);
    }

    private static async Task<TestBuild> BuildAsync(IReadOnlyList<TestPackageBuilder> packages, string extraConfiguration = "")
    {
        var references = string.Join(", ", packages.Select(p => TestBuild.PackageReference(p)));
        var build = TestBuild.Create($$"""{ "version": 1, {{extraConfiguration}} "packages": [ {{references}} ] }""", packages);
        await build.RunSuccessfullyAsync(new AcquisitionStage(), new DiscoveryStage(), new ApiModelStage(), new ApiAggregationStage(), new LinkIndexStage());
        return build;
    }
}
