using System.Diagnostics;
using System.Text.Json;
using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class SearchTests
{
    private static readonly string RankingIndex = CreateIndex(
        ("HttpClient", "a/System.Net.Http.HttpClient.html", "class", "System.Net.Http.HttpClient"),
        ("HttpClientHandler", "a/System.Net.Http.HttpClientHandler.html", "class", "System.Net.Http.HttpClientHandler"),
        ("HttpClient.SendAsync", "a/System.Net.Http.HttpClient.html#SendAsync-overloads", "method", "System.Net.Http.HttpClient.SendAsync"),
        ("IHttpClientFactory", "a/System.Net.Http.IHttpClientFactory.html", "interface", "System.Net.Http.IHttpClientFactory"),
        ("Client", "b/Contoso.Library.Client.html", "class", "Contoso.Library.Client"),
        ("Client.Send", "b/Contoso.Library.Client.html#Send-overloads", "method", "Contoso.Library.Client.Send"),
        ("ClientExtensions", "b/Contoso.Library.ClientExtensions.html", "class", "Contoso.Library.ClientExtensions"),
        ("Mailer.Send", "b/Contoso.Library.Mailer.html#Send", "method", "Contoso.Library.Mailer.Send"),
        ("Tree<T>", "b/Contoso.Collections.Tree-1.html", "class", "Contoso.Collections.Tree<T>"),
        ("Tree<T>.Node", "b/Contoso.Collections.Tree-1.Node.html", "class", "Contoso.Collections.Tree<T>.Node"),
        ("Tree<T>.Node.Child<TKey>", "b/Contoso.Collections.Tree-1.Node.Child-1.html", "class", "Contoso.Collections.Tree<T>.Node.Child<TKey>"),
        ("List<T>", "c/System.Collections.Generic.List-1.html", "class", "System.Collections.Generic.List<T>"),
        ("Contoso.Library", "b/namespaces/Contoso.Library.html", "namespace", "Contoso.Library"),
        ("Getting started", "getting-started.html", "article", ""));

    [Theory]
    [InlineData("Client", "Client")]
    [InlineData("client", "Client")]
    [InlineData("Contoso.Library.Client", "Client")]
    [InlineData("Client.Send", "Client.Send")]
    [InlineData("Library.Client.Send", "Client.Send")]
    [InlineData("HttpClient", "HttpClient")]
    [InlineData("http client", "HttpClient")]
    [InlineData("HttpCli", "HttpClient")]
    [InlineData("hc", "HttpClient")]
    [InlineData("hch", "HttpClientHandler")]
    [InlineData("Tree<T>", "Tree<T>")]
    [InlineData("tree", "Tree<T>")]
    [InlineData("Tree.Node", "Tree<T>.Node")]
    [InlineData("List{T}", "List<T>")]
    [InlineData("SendAsync", "HttpClient.SendAsync")]
    [InlineData("Clinet", "Client")]
    [InlineData("getting", "Getting started")]
    [InlineData("contoso.library", "Contoso.Library")]
    public void Ranking_FirstResult(string query, string expected)
    {
        using var host = new SearchEngineHost(RankingIndex);
        var results = host.Search(query);
        Assert.NotEmpty(results);
        Assert.Equal(expected, results[0]);
    }

    [Fact]
    public void Ranking_AmbiguousShortNamesAreStable()
    {
        using var host = new SearchEngineHost(RankingIndex);

        Assert.Equal(["Client.Send", "Mailer.Send", "HttpClient.SendAsync"], host.Search("send"));
        Assert.Equal(host.Search("send"), host.Search("send"));
        Assert.Equal(["HttpClient", "HttpClientHandler", "HttpClient.SendAsync", "IHttpClientFactory"], host.Search("httpclient"));
    }

    [Fact]
    public void Ranking_ScoresAreOrdered()
    {
        using var host = new SearchEngineHost(RankingIndex);

        Assert.Equal(100, host.SearchDetailed("Client")[0].Score);
        Assert.Equal(95, host.SearchDetailed("Contoso.Library.Client")[0].Score);
        Assert.Equal(90, host.SearchDetailed("Library.Client.Send")[0].Score);
        Assert.Equal(85, host.SearchDetailed("send")[0].Score);
        Assert.Equal(80, host.SearchDetailed("ClientExt")[0].Score);
        Assert.Equal(80, host.SearchDetailed("httpclient")[2].Score);
        Assert.Equal(["Tree<T>.Node", "Tree<T>.Node.Child<TKey>"], host.Search("node"));
        Assert.Equal(70, host.SearchDetailed("node")[1].Score);
        Assert.Equal(60, host.SearchDetailed("http handler")[0].Score);
        Assert.Equal(50, host.SearchDetailed("hc")[0].Score);
        Assert.Equal(20, host.SearchDetailed("Clinet")[0].Score);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzzzzz")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("((((")]
    [InlineData(".*+?^${}()|[]\\")]
    public void NoResultsOrHostileQueries(string query)
    {
        using var host = new SearchEngineHost(RankingIndex);
        Assert.Empty(host.Search(query));
    }

    [Fact]
    public void InvalidIndex_IsAnError()
    {
        Assert.ThrowsAny<Exception>(() => new SearchEngineHost("""{ "version": 99, "items": [] }"""));
    }

    [Fact]
    public async Task GeneratedIndex_ContainsEmittedPagesAndMemberAliases()
    {
        await using var site = await SiteBuilder.BuildAsync([ApiModelTests.CreateKitchenSinkPackage()], files: new() { ["docs/index.md"] = "# Welcome", ["docs/guide.md"] = "# Guide" }, arguments: ["--base-path", "/docs/"]);
        Assert.Equal(ExitCodes.Success, site.Result.ExitCode, site.Result.AllOutput);

        var json = site.ReadFile("_assets/search-index.json");
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.GetProperty("items").EnumerateArray().Select(i => (Title: i[0].GetString()!, Url: i[1].GetString()!, Kind: i[2].GetString()!, Frameworks: i[5].GetString()!)).ToList();

        Assert.Contains(items, i => i is { Title: "Guide", Url: "guide.html", Kind: "article" });
        Assert.Contains(items, i => i is { Title: "Contoso.Library", Url: "api/contoso.library/index.html", Kind: "package" });
        Assert.Contains(items, i => i is { Title: "Client", Url: "api/contoso.library/Contoso.Library.Client.html", Kind: "class" });
        Assert.Contains(items, i => i is { Title: "Client.Send", Url: "api/contoso.library/Contoso.Library.Client.html#Send-overloads", Kind: "method" });
        Assert.Contains(items, i => i is { Title: "Client.Name", Url: "api/contoso.library/Contoso.Library.Client.html#Name", Kind: "property" });
        Assert.Contains(items, i => i is { Title: "ModernOnly", Frameworks: "net8.0" });
        Assert.Single(items, i => i.Title == "Client.Send");
        Assert.Equal(items.Select(i => i.Url).Order(StringComparer.Ordinal), items.Select(i => i.Url));

        // Every URL targets an emitted file and anchor; the page loads the index relative to itself.
        var search = HtmlDocumentLoader.Parse(site.ReadFile("index.html")).QuerySelector("[data-search]")!;
        Assert.Equal("_assets/search-index.json", search.GetAttribute("data-index"));
        Assert.Equal("./", search.GetAttribute("data-base-path"));

        using var host = new SearchEngineHost(json);
        Assert.Equal("api/contoso.library/Contoso.Library.Client.html", host.SearchDetailed("Client")[0].Url);
    }

    [Fact]
    public void PerformanceBudget_LargeCorpus()
    {
        // Budget: at most 160 bytes per entry, and queries on 20,000 entries fast enough to run on each keystroke.
        // The interpreter used here is much slower than a browser engine, so the query budget is checked loosely; the
        // browser tests enforce the interactive budget.
        var items = new List<(string, string, string, string)>();
        for (var i = 0; i < 20_000; i++)
        {
            var type = $"Generated{i % 2000}Type{i}";
            items.Add((i % 10 == 0 ? type : type + ".Member" + i, $"api/p/Contoso.Namespace{i % 50}.{type}.html" + (i % 10 == 0 ? "" : "#Member" + i), i % 10 == 0 ? "class" : "method", $"Contoso.Namespace{i % 50}.{type}"));
        }

        var json = CreateIndex([.. items]);
        Assert.InRange(json.Length / items.Count, 0, 160);

        using var host = new SearchEngineHost(json);
        var stopwatch = Stopwatch.StartNew();
        var results = host.Search("Generated1999Type19999");
        stopwatch.Stop();
        Assert.Equal("Generated1999Type19999.Member19999", results[0]);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    private static string CreateIndex(params (string Title, string Url, string Kind, string QualifiedName)[] items)
    {
        return JsonSerializer.Serialize(new
        {
            version = 2,
            packages = new[] { "Package" },
            items = items.Select(i => new object[] { i.Title, i.Url, i.Kind, 0, i.QualifiedName, "" }),
        });
    }
}
