using Meziantou.DocumentationGenerator.Rendering;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests.EndToEnd;

/// <summary>Release gate: the installed tool generates a valid site from representative packages and content.</summary>
[Collection(InstalledToolCollection.Name)]
public sealed class EndToEndTests(InstalledToolFixture fixture)
{
    [Fact]
    public async Task InstalledTool_GeneratesAValidSite()
    {
        await using var temp = CreateInputs();
        var result = await BuildAsync(temp);

        Assert.Equal(0, result.ExitCode, result.AllOutput);
        var output = temp.FullPath / "_site";
        Assert.Empty(SiteCrawler.Crawl(output, "/", "https://docs.example.com"));

        string[] expectedFiles =
        [
            "index.html", "404.html", "sitemap.xml", "robots.txt", "_assets/search-index.json",
            "guides/getting-started.html", "guides/extensions.html",
            "contoso/api/index.html",
            "contoso/api/Contoso.Library.Client.html", "contoso/api/Contoso.Extensions.SmartClient.html", "contoso/api/Utils.Guard.html",
            "guides/images/diagram.png",
            "undocumented/api/Utils.Guard.html",
        ];
        Assert.All(expectedFiles, file => Assert.True(File.Exists(output / file), file));
        string[] removedFiles = ["contoso/index.html", "meta/index.html", "undocumented/index.html", "meta/api/index.html", "agents.html", "AGENTS.html"];
        Assert.All(removedFiles, file => Assert.False(File.Exists(output / file), file));
        Assert.False(Directory.Exists(output / "contoso" / "_readme"));
        Assert.False(Directory.Exists(output / "01-Guides"));

        // Navigation: authored folders sorted by name, then the API reference by product
        var home = HtmlDocumentLoader.Parse(await File.ReadAllTextAsync(output / "index.html", TestContext.Current.CancellationToken));
        Assert.Equal(["Contoso documentation", "Guides", "API reference"], home.QuerySelectorAll<Meziantou.Framework.WebPlatform.Dom.Element>(".sidebar > ul > li > a, .sidebar > ul > li > .nav-group").Select(e => e.TextContent));
        Assert.Contains("""href="/guides/getting-started.html">guides</a>""", await File.ReadAllTextAsync(output / "index.html", TestContext.Current.CancellationToken));

        // Inherited documentation across packages and source links pinned to the commit
        var smartClient = HtmlDocumentLoader.Parse(await File.ReadAllTextAsync(output / "contoso" / "api" / "Contoso.Extensions.SmartClient.html", TestContext.Current.CancellationToken));
        Assert.Equal("Runs.", smartClient.GetElementById("Run")!.QuerySelector(".summary")!.TextContent!.Trim());
        var client = HtmlDocumentLoader.Parse(await File.ReadAllTextAsync(output / "contoso" / "api" / "Contoso.Library.Client.html", TestContext.Current.CancellationToken));
        Assert.StartsWith($"https://github.com/contoso/library/blob/{SourceLinkTests.Commit}/src/Library.cs#L", client.GetElementById("Send(System.String)")!.QuerySelector(".source-link")!.GetAttribute("href"));

        // Degraded inputs are reported, the fixture's module initializer never ran, and nothing leaks machine paths
        Assert.DoesNotContain("DG3001", result.AllOutput);
        Assert.Contains("DG3002", result.AllOutput);
        Assert.DoesNotContain("The fixture was executed", result.AllOutput);
        AssertNoMachinePaths(output, temp.FullPath);
    }

    [Fact]
    public async Task InstalledTool_SubpathDeployment()
    {
        await using var temp = CreateInputs();
        var result = await BuildAsync(temp, "--base-path", "/docs/");

        Assert.Equal(0, result.ExitCode, result.AllOutput);
        Assert.Empty(SiteCrawler.Crawl(temp.FullPath / "_site", "/docs/", "https://docs.example.com"));
        Assert.False(File.Exists(temp.FullPath / "_site" / "robots.txt"));
    }

    [Fact]
    public async Task RepeatedBuilds_AreDeterministic()
    {
        await using var first = CreateInputs();
        await using var second = CreateInputs(reverseEntries: true, reverseFileCreation: true);
        var firstResult = await BuildAsync(first);
        Assert.Equal(0, firstResult.ExitCode, firstResult.AllOutput);

        // Another working directory, another culture, and package entries and content files written in another order
        var workingDirectory = Directory.CreateDirectory(Path.Combine(fixture.Tool.RootDirectory, "nested", "directory")).FullName;
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["LANG"] = "tr_TR.UTF-8", ["LC_ALL"] = "tr_TR.UTF-8", ["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "false" };
        var secondResult = await fixture.Tool.RunAsync(["build", "--config", second.FullPath / "docgen.json"], workingDirectory, environment, TestContext.Current.CancellationToken);
        Assert.Equal(0, secondResult.ExitCode, secondResult.AllOutput);

        var firstFiles = PublicationTests.GetFiles(first.FullPath / "_site");
        var secondFiles = PublicationTests.GetFiles(second.FullPath / "_site");

        // The manifest records the package hashes, which differ because the archives are written differently
        firstFiles.Remove(OutputManifest.FileName);
        secondFiles.Remove(OutputManifest.FileName);
        Assert.Equal(firstFiles.Keys, secondFiles.Keys);
        Assert.All(firstFiles, file => Assert.Equal(file.Value, secondFiles[file.Key]));
    }

    [Fact]
    public async Task FailedBuild_DoesNotPublishOrLeakCredentials()
    {
        await using var temp = CreateInputs();
        Assert.Equal(0, (await BuildAsync(temp)).ExitCode);
        var before = PublicationTests.GetFiles(temp.FullPath / "_site");

        temp.CreateTextFile("docgen.json", FixturePackages.Configuration.Replace("\"version\": \"1.0.0\", \"path\": \"packages/contoso.meta.1.0.0.nupkg\"", "\"version\": \"9.9.9\", \"source\": \"https://user:p4ssw0rd@feed.example.com/v3/index.json\"", StringComparison.Ordinal));
        var result = await BuildAsync(temp);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("DG1011", result.AllOutput);
        Assert.DoesNotContain("p4ssw0rd", result.AllOutput);
        Assert.Equal(before, PublicationTests.GetFiles(temp.FullPath / "_site"));
    }

    private Task<ProcessResult> BuildAsync(TemporaryDirectory directory, params string[] arguments)
    {
        return fixture.Tool.RunAsync(["build", "--config", directory.FullPath / "docgen.json", .. arguments], TestContext.Current.CancellationToken);
    }

    private static TemporaryDirectory CreateInputs(bool reverseEntries = false, bool reverseFileCreation = false)
    {
        var temp = TemporaryDirectory.Create();
        foreach (var package in FixturePackages.Create(reverseEntries))
        {
            package.Save(temp.FullPath / "packages");
        }

        var files = FixturePackages.Content.ToList();
        if (reverseFileCreation)
        {
            files.Reverse();
        }

        foreach (var (path, content) in files)
        {
            temp.CreateTextFile(path, content);
        }

        temp.CreateTextFile("docgen.json", FixturePackages.Configuration);
        return temp;
    }

    private static void AssertNoMachinePaths(FullPath output, FullPath inputDirectory)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".png", StringComparison.Ordinal)))
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain(inputDirectory.Value, content);
            Assert.DoesNotContain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), content);
            if (home.Length > 1)
            {
                Assert.DoesNotContain(home, content);
            }

            if (!OperatingSystem.IsWindows())
            {
                Assert.DoesNotContain("/private/", content);
            }
        }
    }
}
