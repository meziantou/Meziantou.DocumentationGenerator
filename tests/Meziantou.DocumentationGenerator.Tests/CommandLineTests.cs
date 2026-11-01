using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Cli;
using Meziantou.DocumentationGenerator.Configuration;
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
        Assert.False(File.Exists(temp.FullPath / "docs" / "toc.yml"));
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
        temp.CreateTextFile("docs/index.md", "# Home");
        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json", "--content-root", temp.FullPath / "docs"]);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("DG1007", result.Error);
    }

    [Fact]
    public async Task Build_WithoutConfigurationFile()
    {
        await using var temp = TemporaryDirectory.Create();
        temp.CreateTextFile("docs/index.md", "# CLI site\n\n[Unavailable](cref:T:Missing.Type)");
        var testAssembly = typeof(CommandLineTests).Assembly.Location;
        var result = await ProcessRunner.RunAsync(ProcessRunner.DotnetPath,
            ["exec", "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"), "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"),
                typeof(CommandLineApplication).Assembly.Location, "build", "--content-root", "docs", "--site-title", "CLI site", "--cache-directory", "cache",
                "--output", "_site", "--allow-invalid-cref"],
            temp.FullPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.Contains("warning DG4001", result.Output);
        var html = await File.ReadAllTextAsync(temp.FullPath / "_site" / "index.html", TestContext.Current.CancellationToken);
        Assert.Contains("CLI site", html);
        Assert.Contains("Unavailable", html);
        Assert.DoesNotContain("cref:", html);
        Assert.False(File.Exists(temp.FullPath / "docgen.json"));
    }

    [Fact]
    public async Task Build_UsesDefaultConfigurationFileWhenPresent()
    {
        await using var temp = CreateSite("""{"version":1,"content":{"root":"docs"},"site":{"title":"Default file"}}""");
        var testAssembly = typeof(CommandLineTests).Assembly.Location;
        var result = await ProcessRunner.RunAsync(ProcessRunner.DotnetPath,
            ["exec", "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"), "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"),
                typeof(CommandLineApplication).Assembly.Location, "build", "--cache-directory", "cache"],
            temp.FullPath, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        var html = await File.ReadAllTextAsync(temp.FullPath / "_site" / "index.html", TestContext.Current.CancellationToken);
        Assert.Contains("Default file", html);
    }

    [Fact]
    public async Task VersionArguments_AreScopedToTheirCommands()
    {
        var result = await CliRunner.RunAsync(["--version"]);
        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.NotEmpty(result.Output.Trim());

        await using var temp = CreateSite(MinimalConfiguration);
        result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json", "--version", "2"]);
        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode);
        Assert.Contains("DG1001", result.Error);
    }

    [Fact]
    public void ConfigurationOptions_ExposeAllProperties()
    {
        const string Json = """
            {
              "$schema": "https://example.com/schema.json",
              "version": 1,
              "site": {
                "title": "CLI title", "description": "CLI description", "language": "fr", "basePath": "/docs/",
                "url": "https://example.com", "logo": "logo.png", "favicon": "favicon.ico", "socialImage": "social.png",
                "robots": "disallow", "footer": "CLI footer",
                "headerLinks": [{"text":"GitHub","url":"https://github.com","icon":"GH"}], "minifyHtml": true
              },
              "output": "output", "cacheDirectory": "cache",
              "packages": [{"id":"Contoso","version":"1.0","source":"https://example.com/v3/index.json","path":"packages",
                "targetFrameworks":["net8.0"],"assemblies":["Contoso.dll"],"defineConstants":["CUSTOM"]}],
              "references": [{"id":"Reference","version":"2.*","source":"local","path":"refs",
                "targetFrameworks":["net9.0"],"assemblies":["Reference.dll"],"defineConstants":["REFERENCE"]}],
              "content": {"root":"pages","assets":["CNAME"],"exclude":["**/AGENTS.md"],"editUrl":"https://example.com/{path}"},
              "api": {"include":["Contoso.*"],"exclude":["T:Contoso.Hidden"]},
              "xrefs": [{"namespace":"Other","url":"https://example.com/{uid}"}],
              "diagnostics": {"allowInvalidCref":true,"warningsAsErrors":false,"severity":{"DG5010":"none"}},
              "sourceLinks": {"enabled":false,"symbolServers":["https://symbols.example.com"]}
            }
            """;
        string[] args =
        [
            "--schema", "https://example.com/schema.json", "--version", "1",
            "--site-title", "CLI title", "--site-description", "CLI description", "--site-language", "fr",
            "--site-base-path", "/docs/", "--site-url", "https://example.com", "--site-logo", "logo.png",
            "--site-favicon", "favicon.ico", "--site-social-image", "social.png", "--site-robots", "disallow",
            "--site-footer", "CLI footer", "--site-header-links", """[{"text":"GitHub","url":"https://github.com","icon":"GH"}]""",
            "--site-minify-html", "true", "--output", "output", "--cache-directory", "cache",
            "--packages", """[{"id":"Contoso","version":"1.0","source":"https://example.com/v3/index.json","path":"packages","targetFrameworks":["net8.0"],"assemblies":["Contoso.dll"],"defineConstants":["CUSTOM"]}]""",
            "--references", """[{"id":"Reference","version":"2.*","source":"local","path":"refs","targetFrameworks":["net9.0"],"assemblies":["Reference.dll"],"defineConstants":["REFERENCE"]}]""",
            "--content-root", "pages", "--content-assets", """["CNAME"]""", "--content-exclude", """["**/AGENTS.md"]""",
            "--content-edit-url", "https://example.com/{path}", "--api-include", """["Contoso.*"]""",
            "--api-exclude", """["T:Contoso.Hidden"]""", "--xrefs", """[{"namespace":"Other","url":"https://example.com/{uid}"}]""",
            "--diagnostics-allow-invalid-cref", "true", "--diagnostics-warnings-as-errors", "false",
            "--diagnostics-severity", """{"DG5010":"none"}""", "--source-links-enabled", "false",
            "--source-links-symbol-servers", """["https://symbols.example.com"]""",
        ];
        var command = new Command("build");
        var options = new ConfigurationCommandLineOptions(command);
        var result = command.Parse(args);

        Assert.Empty(result.Errors);
        Assert.Equal(command.Options.Count, args.Length / 2);
        var actual = JsonSerializer.SerializeToNode(options.GetOverrides(result).Values, ConfigurationLoader.SerializerOptions);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Json), actual), actual?.ToJsonString());
    }

    [Theory]
    [InlineData("--packages", "{}")]
    [InlineData("--packages", "[")]
    [InlineData("--packages", """[{"unknown":true}]""")]
    [InlineData("--content-assets", "images")]
    [InlineData("--diagnostics-severity", "[]")]
    [InlineData("--source-links-enabled", "yes")]
    [InlineData("--source-links-enabled", "null")]
    [InlineData("--version", "invalid")]
    public async Task Build_InvalidConfigurationArgument(string option, string value)
    {
        var result = await CliRunner.RunAsync(["build", option, value]);

        Assert.Equal(ExitCodes.InvalidArguments, result.ExitCode);
        Assert.Contains(option, result.Error);
    }

    [Fact]
    public async Task Build_OverridesMergeSectionsAndReplaceCollections()
    {
        await using var temp = CreateSite("""
            {
              "version":1,
              "site":{"title":"File title","description":"Keep description"},
              "content":{"root":"docs","exclude":["old.md"]},
              "packages":[{"id":"Old"}],
              "references":[{"id":"OldReference"}],
              "xrefs":[{"namespace":"Old","url":"https://example.com/{uid}"}],
              "diagnostics":{"warningsAsErrors":true,"severity":{"DG5010":"error"}},
              "sourceLinks":{"enabled":false,"symbolServers":["https://symbols.example.com"]}
            }
            """);
        var inspected = false;
        var result = await CliRunner.RunAsync(
            ["build", "--config", temp.FullPath / "docgen.json", "--site-title", "CLI title", "--content-exclude", "[]",
                "--packages", "[]", "--references", "[]", "--xrefs", "[]", "--diagnostics-warnings-as-errors", "false",
                "--diagnostics-severity", """{"DG4001":"info"}""", "--source-links-enabled", "true"],
            _ => [new DelegateStage("Inspect", (context, _) =>
            {
                inspected = true;
                Assert.Equal("CLI title", context.Configuration.Site.Title);
                Assert.Equal("Keep description", context.Configuration.Site.Description);
                Assert.Equal(temp.FullPath / "docs", context.Configuration.Content!.Root);
                Assert.Empty(context.Configuration.Content.Exclude);
                Assert.Empty(context.Configuration.Packages);
                Assert.Empty(context.Configuration.References);
                Assert.Empty(context.Configuration.ExternalReferences);
                Assert.False(context.Configuration.Diagnostics.WarningsAsErrors);
                Assert.Equal(DiagnosticSeverity.Info, context.Configuration.Diagnostics.GetSeverity(DiagnosticDescriptors.UnresolvedReference));
                Assert.Equal(DiagnosticSeverity.Info, context.Configuration.Diagnostics.GetSeverity(DiagnosticDescriptors.UnknownCodeLanguage));
                Assert.True(context.Configuration.SourceLinks.Enabled);
                Assert.Single(context.Configuration.SourceLinks.SymbolServers);
                context.Diagnostics.Report(DiagnosticDescriptors.RobotsUnderSubpath, "still a warning");
                return Task.CompletedTask;
            })]);

        Assert.Equal(ExitCodes.Success, result.ExitCode, result.AllOutput);
        Assert.True(inspected);
    }

    [Theory]
    [InlineData(false, null, false, ExitCodes.BuildFailed)]
    [InlineData(true, null, false, ExitCodes.Success)]
    [InlineData(false, "true", false, ExitCodes.Success)]
    [InlineData(true, "false", false, ExitCodes.BuildFailed)]
    [InlineData(true, null, true, ExitCodes.BuildFailed)]
    public async Task Build_AllowInvalidCref(bool configured, string? commandLineValue, bool warningsAsErrors, int expectedExitCode)
    {
        string[] arguments = [
            .. commandLineValue is null ? Array.Empty<string>() : ["--allow-invalid-cref", commandLineValue],
            .. warningsAsErrors ? new[] { "--warnaserror" } : [],
        ];
        await using var site = await SiteBuilder.BuildAsync([], files: new()
        {
            ["docs/index.md"] = "# Home\n\n[Unavailable](cref:T:Missing.Type)",
        }, extraConfiguration: $$""" "diagnostics":{"allowInvalidCref":{{(configured ? "true" : "false")}}}, """, arguments: arguments);

        Assert.Equal(expectedExitCode, site.Result.ExitCode, site.Result.AllOutput);
        Assert.Contains("DG4001", site.Result.AllOutput);
        if (expectedExitCode == ExitCodes.Success)
        {
            var html = site.ReadFile("index.html");
            Assert.Contains("Unavailable", html);
            Assert.DoesNotContain("cref:", html);
            Assert.Contains("warning DG4001", site.Result.Output);
        }
        else
        {
            Assert.False(File.Exists(site.Output / "index.html"));
        }
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

    [Theory]
    [InlineData("--warnaserror")]
    [InlineData("--diagnostics-warnings-as-errors")]
    public async Task Build_CommandLineWarningsAsErrorsAppliesDuringConfiguration(string option)
    {
        await using var temp = CreateSite("""{"version":1,"content":{"root":"docs"},"site":{"basePath":"/docs/","robots":"allow"}}""");
        var executed = false;
        var result = await CliRunner.RunAsync(["build", "--config", temp.FullPath / "docgen.json", option], _ =>
            [new DelegateStage("Inspect", (_, _) => { executed = true; return Task.CompletedTask; })]);

        Assert.Equal(ExitCodes.BuildFailed, result.ExitCode, result.AllOutput);
        Assert.Contains("error DG1010", result.Error);
        Assert.False(executed);
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
