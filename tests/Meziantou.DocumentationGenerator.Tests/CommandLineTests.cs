using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests;

public sealed class CommandLineTests
{
    private const string MinimalConfiguration = """{ "version": 1, "content": { "root": "docs" } }""";

    [Fact]
    public async Task Init_CreatesConfigurationAndContent()
    {
        await using var temp = TemporaryDirectory.Create();
        var result = await CliRunner.RunAsync(["init", temp.FullPath, "--package", "Contoso.Json", "--package-version", "2.1.0", "--title", "Contoso"]);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.Contains("\"id\": \"Contoso.Json\"", await File.ReadAllTextAsync(temp.FullPath / "docgen.json", TestContext.Current.CancellationToken));
        Assert.True(File.Exists(temp.FullPath / "docs" / "index.md"));
        Assert.True(File.Exists(temp.FullPath / "docs" / "toc.yml"));
    }

    [Fact]
    public async Task Init_DoesNotOverwriteExistingFiles()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docgen.json", "existing");
        temp.CreateTextFile("docs/index.md", "my page");

        var result = await CliRunner.RunAsync(["init", temp.FullPath]);
        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("already exists", result.Error);
        Assert.Equal("existing", await File.ReadAllTextAsync(temp.FullPath / "docgen.json", TestContext.Current.CancellationToken));

        result = await CliRunner.RunAsync(["init", temp.FullPath, "--force"]);
        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.NotEqual("existing", await File.ReadAllTextAsync(temp.FullPath / "docgen.json", TestContext.Current.CancellationToken));
        Assert.Equal("my page", await File.ReadAllTextAsync(temp.FullPath / "docs" / "index.md", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Build_RunsStagesInOrder()
    {
        await using var temp = CreateSite(MinimalConfiguration);
        var executed = new List<string>();
        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json"], _ =>
        [
            new DelegateStage("First", (_, _) => { executed.Add("First"); return Task.CompletedTask; }),
            new DelegateStage("Second", (context, _) => { executed.Add("Second"); context.IsPublished = true; context.GeneratedFileCount = 3; return Task.CompletedTask; }),
        ]);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.Equal(["First", "Second"], executed);
        Assert.Contains("Build succeeded: 3 file(s) published", result.Output);
    }

    [Fact]
    public async Task Build_InvalidArguments()
    {
        var result = await CliRunner.RunAsync(["build", "--unknown"]);
        Assert.Equal(ExitCodes.InvalidArguments, result.ExitCode);

        result = await CliRunner.RunAsync(["build", "--verbosity", "loud"]);
        Assert.Equal(ExitCodes.InvalidArguments, result.ExitCode);
    }

    [Fact]
    public async Task Build_InvalidConfiguration()
    {
        await using var temp = CreateSite("""{ "version": 1, "content": { "root": "docs" }, "unknown": true }""");
        var stageExecuted = false;
        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json"], _ => [new DelegateStage("Stage", (_, _) => { stageExecuted = true; return Task.CompletedTask; })]);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.False(stageExecuted);
        Assert.Contains("docgen.json(1,48), $.unknown: error DG1002", result.Error);
        Assert.Contains("Build failed: the output directory was not modified. 1 error(s), 0 warning(s)", result.Error);
    }

    [Fact]
    public async Task Build_MissingConfiguration()
    {
        await using var temp = TemporaryDirectory.Create();
        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json"]);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("DG1007", result.Error);
    }

    [Fact]
    public async Task Build_WarningsArePromotedWithWarnAsError()
    {
        await using var temp = CreateSite(MinimalConfiguration);
        Func<BuildContext, IReadOnlyList<IBuildStage>> stages = _ =>
        [
            new DelegateStage("Warn", (context, _) => { context.Diagnostics.Report(DiagnosticDescriptors.RobotsUnderSubpath, "a warning"); return Task.CompletedTask; }),
            new DelegateStage("Publish", (context, _) => { context.IsPublished = true; return Task.CompletedTask; }),
        ];

        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json"], stages);
        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.Contains("warning DG1010: a warning", result.Output);

        result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json", "--warnaserror"], stages);
        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("error DG1010: a warning", result.Error);
        Assert.DoesNotContain("published", result.AllOutput);
    }

    [Fact]
    public async Task Build_SeverityOverridesFromConfiguration()
    {
        await using var temp = CreateSite("""{ "version": 1, "content": { "root": "docs" }, "diagnostics": { "severity": { "DG1010": "error" } } }""");
        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json"], _ =>
            [new DelegateStage("Warn", (context, _) => { context.Diagnostics.Report(DiagnosticDescriptors.RobotsUnderSubpath, "now an error"); return Task.CompletedTask; })]);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("error DG1010: now an error", result.Error);
    }

    [Fact]
    public async Task Build_StageFailure_StopsBeforePublication()
    {
        await using var temp = CreateSite(MinimalConfiguration);
        var published = false;
        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json"], _ =>
        [
            new DelegateStage("Render", (_, _) => throw new InvalidOperationException("boom")),
            new DelegateStage("Publish", (_, _) => { published = true; return Task.CompletedTask; }),
        ]);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.False(published);
        Assert.Contains("error DG7001: The 'Render' stage failed: InvalidOperationException: boom", result.Error);
    }

    [Fact]
    public async Task Build_Cancellation_StopsBeforePublication()
    {
        await using var temp = CreateSite(MinimalConfiguration);
        using var cts = new CancellationTokenSource();
        var published = false;
        var result = await CliRunner.RunWithCancellationAsync(["build", "--config", temp.FullPath / "docgen.json"], _ =>
        [
            new DelegateStage("Long", async (_, cancellationToken) => { await cts.CancelAsync(); cancellationToken.ThrowIfCancellationRequested(); }),
            new DelegateStage("Publish", (_, _) => { published = true; return Task.CompletedTask; }),
        ], cts.Token);

        Assert.Equal(ExitCodes.Cancelled, result.ExitCode);
        Assert.False(published);
        Assert.Contains("Build cancelled", result.Error);
    }

    [Fact]
    public async Task Build_OutputOverride_IsRelativeToCurrentDirectory()
    {
        await using var temp = CreateSite(MinimalConfiguration);
        FullPath? output = null;
        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json", "--output", temp.FullPath / "custom", "--base-path", "docs"], _ =>
            [new DelegateStage("Inspect", (context, _) => { output = context.Configuration.OutputDirectory; Assert.Equal("/docs/", context.Configuration.Site.BasePath.Value); return Task.CompletedTask; })]);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.Equal(temp.FullPath / "custom", output);
    }

    [Fact]
    public async Task Build_DiagnosticsAreStable()
    {
        await using var temp = CreateSite(MinimalConfiguration);
        string? previous = null;
        for (var i = 0; i < 3; i++)
        {
            var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json", "-v", "detailed"], _ =>
            [
                new DelegateStage("Parallel", (context, _) =>
                {
                    Parallel.For(0, 50, j => context.Diagnostics.Report(DiagnosticDescriptors.RobotsUnderSubpath, $"message {j % 7}", new DiagnosticLocation { FilePath = $"file{j % 5}.md", Line = j }));
                    return Task.CompletedTask;
                }),
            ]);

            var diagnostics = string.Join('\n', result.Output.Split('\n').Where(line => line.Contains("DG1010", StringComparison.Ordinal)));
            if (previous is not null)
            {
                Assert.Equal(previous, diagnostics);
            }

            previous = diagnostics;
        }
    }

    [Fact]
    public async Task Build_QuietVerbosity_HidesWarnings()
    {
        await using var temp = CreateSite(MinimalConfiguration);
        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json", "--verbosity", "quiet"], _ =>
            [new DelegateStage("Warn", (context, _) => { context.Diagnostics.Report(DiagnosticDescriptors.RobotsUnderSubpath, "hidden"); return Task.CompletedTask; })]);

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.DoesNotContain("hidden", result.AllOutput);
        Assert.DoesNotContain("Configuration...", result.AllOutput);
        Assert.Contains("1 warning(s)", result.Output);
    }

    [Fact]
    public async Task DiagnosticsDocumentation_IsUpToDate()
    {
        var path = FullPath.FromPath(RepositoryPaths.Root) / "docs" / "diagnostics.md";
        var actual = File.Exists(path) ? (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n") : "";
        var missing = DiagnosticDescriptors.GetAll().Where(d => !actual.Contains($"| `{d.Code}` |", StringComparison.Ordinal)).Select(d => d.Code).ToList();
        Assert.Empty(missing);
    }

    private static TemporaryDirectory CreateSite(string configuration)
    {
        var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docgen.json", configuration);
        temp.CreateTextFile("docs/index.md", "# Home");
        return temp;
    }
}
