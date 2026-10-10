using System.Text;
using System.Text.RegularExpressions;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Tests.EndToEnd;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests;

/// <summary>Keeps the documentation, the sample and the quick start in sync with the tool.</summary>
[Collection(InstalledToolCollection.Name)]
public sealed partial class DocumentationTests(InstalledToolFixture fixture)
{
    public static TheoryData<string, int> ConfigurationExamples()
    {
        var data = new TheoryData<string, int>();
        foreach (var file in GetDocumentationFiles())
        {
            var matches = JsonBlockRegex().Matches(File.ReadAllText(Path.Combine(RepositoryPaths.Root, file)));
            for (var i = 0; i < matches.Count; i++)
            {
                if (matches[i].Groups["json"].Value.Contains("\"version\": 1", StringComparison.Ordinal))
                {
                    data.Add(file, i);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ConfigurationExamples))]
    public async Task ConfigurationExamples_AreValid(string file, int index)
    {
        var json = JsonBlockRegex().Matches(await File.ReadAllTextAsync(Path.Combine(RepositoryPaths.Root, file), TestContext.Current.CancellationToken))[index].Groups["json"].Value;
        await using var temp = TemporaryDirectory.Create();
        temp.CreateDirectory("docs");
        var diagnostics = new DiagnosticBag();
        ConfigurationLoader.Load(Encoding.UTF8.GetBytes(json), temp.FullPath / "docgen.json", diagnostics);

        // Examples reference files that do not exist in the temporary directory, and some are fragments without inputs
        Assert.Empty(diagnostics.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Code is not ("DG1007" or "DG1003")).Select(d => d.ToString()));
    }

    [Fact]
    public async Task QuickStart_InitAndBuild()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var temp = TemporaryDirectory.Create();

        var init = await fixture.Tool.RunAsync(["init", temp.FullPath, "--title", "Contoso"], cancellationToken);
        Assert.Equal(0, init.ExitCode, init.AllOutput);
        var build = await fixture.Tool.RunAsync(["build", "--config", temp.FullPath / "docgen.json"], cancellationToken);
        Assert.Equal(0, build.ExitCode, build.AllOutput);
        Assert.True(File.Exists(temp.FullPath / "_site" / "index.html"));
        Assert.Empty(SiteCrawler.Crawl(temp.FullPath / "_site", "/", siteUrl: null));
    }

    [Fact]
    public async Task Sample_BuildsFromSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var temp = TemporaryDirectory.Create();
        var sample = FullPath.FromPath(RepositoryPaths.Root) / "samples" / "basic";
        CopyDirectory(sample, temp.FullPath, exclude: ["artifacts", "bin", "obj"]);
        temp.CreateTextFile("Directory.Build.props", "<Project />");
        temp.CreateTextFile("Directory.Packages.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
        File.Copy(FullPath.FromPath(RepositoryPaths.Root) / "global.json", temp.FullPath / "global.json");

        var pack = await ProcessRunner.RunAsync(ProcessRunner.DotnetPath, ["pack", temp.FullPath / "src" / "Sample.Library", "--output", temp.FullPath / "artifacts" / "packages", "--disable-build-servers"], temp.FullPath, environment: null, cancellationToken);
        Assert.Equal(0, pack.ExitCode, pack.AllOutput);

        var build = await fixture.Tool.RunAsync(["build", "--config", temp.FullPath / "docgen.json", "--warnaserror"], cancellationToken);
        Assert.Equal(0, build.ExitCode, build.AllOutput);
        var output = temp.FullPath / "artifacts" / "site";
        Assert.Empty(SiteCrawler.Crawl(output, "/", siteUrl: null));
        var greeter = await File.ReadAllTextAsync(output / "sample" / "api" / "Sample.Library.Greeter.html", cancellationToken);
        Assert.Contains("net10.0", greeter);
        Assert.Contains("net11.0", greeter);
        var loud = HtmlDocumentLoader.Parse(await File.ReadAllTextAsync(output / "sample" / "api" / "Sample.Library.LoudGreeter.html", cancellationToken));
        Assert.Equal("Returns a greeting using a custom salutation.", loud.GetElementById("Greet(System.String)")!.QuerySelector(".summary")!.TextContent.Trim());
    }

    private static IEnumerable<string> GetDocumentationFiles()
    {
        yield return "README.md";
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "docs"), "*.md").Order(StringComparer.Ordinal))
        {
            yield return "docs/" + Path.GetFileName(file);
        }
    }

    private static void CopyDirectory(FullPath source, FullPath destination, string[] exclude)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(segment => exclude.Contains(segment, StringComparer.Ordinal)))
                continue;

            var target = destination / relative;
            target.CreateParentDirectory();
            File.Copy(file, target);
        }
    }

    [GeneratedRegex("```json\\r?\\n(?<json>.*?)\\r?\\n```", RegexOptions.Singleline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JsonBlockRegex();
}
