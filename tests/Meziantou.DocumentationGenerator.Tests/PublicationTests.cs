using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Rendering;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class PublicationTests
{
    [Fact]
    public async Task GeneratesACompleteStaticSite()
    {
        await using var temp = CreateSite();
        var result = await BuildAsync(temp);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        var output = temp.FullPath / "_site";
        Assert.True(File.Exists(output / "index.html"));
        Assert.True(File.Exists(output / "guide.html"));
        Assert.True(File.Exists(output / "_assets" / "site.css"));
        Assert.True(File.Exists(output / OutputManifest.FileName));

        var html = await File.ReadAllTextAsync(output / "guide.html", TestContext.Current.CancellationToken);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title>&lt;script&gt;alert(1)&lt;/script&gt; | Test site</title>", html);
        Assert.Contains("<h1>&lt;script&gt;alert(1)&lt;/script&gt;</h1>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/_assets/site.css\" />", html);
        Assert.DoesNotContain("blazor", html, ignoreCase: true);
        Assert.DoesNotContain("_framework", html);
        Assert.DoesNotContain("<!--!-->", html);
        Assert.Contains("Build succeeded", result.Output);
    }

    [Fact]
    public async Task RootAndSubpath_HaveTheSameLogicalContent()
    {
        await using var root = CreateSite();
        await using var docs = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(root)).ExitCode);
        Assert.Equal(ExitCodes.Success, (await BuildAsync(docs, "--base-path", "/docs/")).ExitCode);

        var rootFiles = GetFiles(root.FullPath / "_site");
        var docsFiles = GetFiles(docs.FullPath / "_site");
        Assert.Equal(rootFiles.Keys, docsFiles.Keys);
        Assert.Contains("href=\"/docs/guide.html\"", docsFiles["index.html"]);
        Assert.Equal(rootFiles["guide.html"], docsFiles["guide.html"].Replace("\"/docs/", "\"/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatedBuilds_AreByteIdentical()
    {
        await using var first = CreateSite();
        await using var second = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(first)).ExitCode);
        Assert.Equal(ExitCodes.Success, (await BuildAsync(second)).ExitCode);
        Assert.Equal(ExitCodes.Success, (await BuildAsync(second)).ExitCode);

        Assert.Equal(GetFiles(first.FullPath / "_site"), GetFiles(second.FullPath / "_site"));
    }

    [Fact]
    public async Task FailedBuild_KeepsThePreviousSite()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);
        var before = GetFiles(temp.FullPath / "_site");

        temp.CreateTextFile("docs/broken.md", "[missing](missing.md)");
        var result = await BuildAsync(temp);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("DG5001", result.Error);
        Assert.Equal(before, GetFiles(temp.FullPath / "_site"));
        AssertNoStagingDirectory(temp);
    }

    [Fact]
    public async Task StaleFilesAreRemoved_UnrelatedFilesSurvive()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);
        temp.CreateTextFile("_site/CNAME", "docs.example.com");
        temp.CreateTextFile("_site/custom/keep.txt", "keep");

        File.Delete(temp.FullPath / "docs" / "guide.md");
        temp.CreateTextFile("docs/index.md", "# Home");
        var result = await BuildAsync(temp);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.False(File.Exists(temp.FullPath / "_site" / "guide.html"));
        Assert.True(File.Exists(temp.FullPath / "_site" / "CNAME"));
        Assert.True(File.Exists(temp.FullPath / "_site" / "custom" / "keep.txt"));
        AssertNoStagingDirectory(temp);
    }

    [Fact]
    public async Task UnrelatedFileCollision_FailsWithoutChanges()
    {
        await using var temp = CreateSite();
        temp.CreateTextFile("_site/guide.html", "user file");
        temp.CreateTextFile("_site/other.txt", "other");

        var result = await BuildAsync(temp);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("DG7004", result.Error);
        Assert.Contains("guide.html", result.Error);
        Assert.Equal("user file", await File.ReadAllTextAsync(temp.FullPath / "_site" / "guide.html", TestContext.Current.CancellationToken));
        Assert.False(File.Exists(temp.FullPath / "_site" / "index.html"));
    }

    [Fact]
    public async Task InterruptedPublication_IsCompletedByTheNextBuild()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);

        // A previous publication was interrupted after writing a file that is not in the manifest yet
        temp.CreateTextFile("_site/orphan.html", "generated by an interrupted build");
        temp.CreateTextFile("_site/" + OutputPublisher.JournalFileName, """{ "version": 1, "files": [ "orphan.html" ] }""");

        var result = await BuildAsync(temp);
        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.False(File.Exists(temp.FullPath / "_site" / "orphan.html"));
        Assert.False(File.Exists(temp.FullPath / "_site" / OutputPublisher.JournalFileName));
    }

    [Fact]
    public async Task Cancellation_KeepsThePreviousSite()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);
        var before = GetFiles(temp.FullPath / "_site");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var result = await CliRunner.RunWithCancellationAsync(["build", "--config", temp.FullPath / "docgen.json"], stages: null, cts.Token);

        Assert.Equal(ExitCodes.Cancelled, result.ExitCode);
        Assert.Equal(before, GetFiles(temp.FullPath / "_site"));
        AssertNoStagingDirectory(temp);
    }

    [Fact]
    public async Task Manifest_ListsFilesWithoutTimestampsOrMachinePaths()
    {
        await using var temp = CreateSite();
        Assert.Equal(ExitCodes.Success, (await BuildAsync(temp)).ExitCode);

        var manifest = OutputManifest.TryRead(temp.FullPath / "_site" / OutputManifest.FileName)!;
        Assert.Equal(["_assets/site.css", "_assets/site.js", "guide.html", "index.html"], manifest.Files.Where(f => !f.StartsWith("_assets/search", StringComparison.Ordinal) && f != "404.html" && f != "sitemap.xml"));
        var text = await File.ReadAllTextAsync(temp.FullPath / "_site" / OutputManifest.FileName, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(temp.FullPath.Value, text);
        Assert.DoesNotContain(DateTime.UtcNow.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-", text);
    }

    internal static TemporaryDirectory CreateSite()
    {
        var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docgen.json", """{ "version": 1, "site": { "title": "Test site" }, "content": { "root": "docs" } }""");
        temp.CreateTextFile("docs/index.md", "# Home\n\nRead the [guide](guide.md).");
        temp.CreateTextFile("docs/guide.md", "---\ntitle: <script>alert(1)</script>\nslug: guide\n---\n\nSome **bold** text.");
        return temp;
    }

    internal static Task<ProcessResult> BuildAsync(TemporaryDirectory directory, params string[] arguments)
    {
        return CliRunner.RunAsync(["build", "--config", directory.FullPath / "docgen.json", .. arguments]);
    }

    internal static SortedDictionary<string, string> GetFiles(FullPath directory)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(directory, file).Replace('\\', '/'), File.ReadAllText(file));
        }

        return result;
    }

    private static void AssertNoStagingDirectory(TemporaryDirectory directory)
    {
        Assert.Empty(Directory.EnumerateDirectories(directory.FullPath, ".*staging*"));
    }
}
