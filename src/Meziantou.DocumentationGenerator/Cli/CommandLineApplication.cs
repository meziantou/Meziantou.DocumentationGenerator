using System.CommandLine;
using System.Globalization;
using System.Net;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Preview;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Cli;

internal sealed class CommandLineApplication
{
    private const int DefaultPreviewPort = 8080;

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Func<BuildContext, IReadOnlyList<IBuildStage>> _stageFactory;

    public CommandLineApplication(TextWriter output, TextWriter error, Func<BuildContext, IReadOnlyList<IBuildStage>>? stageFactory = null)
    {
        _output = output;
        _error = error;
        _stageFactory = stageFactory ?? (_ => BuildPipeline.CreateStages());
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var rootCommand = CreateRootCommand();
        var parseResult = rootCommand.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
            {
                _error.WriteLine(error.Message);
            }

            _error.WriteLine("Run 'Meziantou.DocumentationGenerator --help' for usage information.");
            return ExitCodes.InvalidArguments;
        }

        return await parseResult.InvokeAsync(new InvocationConfiguration { Output = _output, Error = _error, EnableDefaultExceptionHandler = false }, cancellationToken);
    }

    private RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Generate a fully static documentation site from NuGet packages and Markdown pages.");
        rootCommand.Subcommands.Add(CreateInitCommand());
        rootCommand.Subcommands.Add(CreateBuildCommand());
        return rootCommand;
    }

    private Command CreateInitCommand()
    {
        var directoryArgument = new Argument<string?>("directory")
        {
            Description = "Directory where docgen.json is created. Default: the current directory.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var packageOption = new Option<string?>("--package") { Description = "Identifier of a package to document." };
        var versionOption = new Option<string?>("--package-version") { Description = "Version or version range of the package. Default: the latest stable version." };
        var titleOption = new Option<string?>("--title") { Description = "Site title." };
        var forceOption = new Option<bool>("--force") { Description = "Overwrite an existing docgen.json. Existing Markdown files are never overwritten." };

        var command = new Command("init", "Create a docgen.json configuration file and a minimal content directory.")
        {
            directoryArgument,
            packageOption,
            versionOption,
            titleOption,
            forceOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var directory = FullPath.FromPath(parseResult.GetValue(directoryArgument) ?? ".");
            var configurationPath = directory / ConfigurationLoader.DefaultFileName;
            if (File.Exists(configurationPath) && !parseResult.GetValue(forceOption))
            {
                _error.WriteLine($"'{configurationPath}' already exists. Use --force to overwrite it.");
                return ExitCodes.BuildFailed;
            }

            Directory.CreateDirectory(directory);
            var content = ConfigurationTemplate.Create(parseResult.GetValue(packageOption), parseResult.GetValue(versionOption), parseResult.GetValue(titleOption));
            await File.WriteAllTextAsync(configurationPath, content, cancellationToken);
            _output.WriteLine($"Created {ConfigurationLoader.DefaultFileName}");

            await CreateFileIfMissingAsync(directory, "docs/index.md", """
                ---
                title: Home
                ---

                # Welcome

                This page is the home page of the site. Edit `docs/index.md` to change it.
                """, cancellationToken);

            _output.WriteLine("Run 'Meziantou.DocumentationGenerator build' to generate the site.");
            return ExitCodes.Success;
        });

        return command;
    }

    private async Task CreateFileIfMissingAsync(FullPath directory, string relativePath, string content, CancellationToken cancellationToken)
    {
        var path = directory / relativePath;
        if (File.Exists(path))
            return;

        path.CreateParentDirectory();
        await File.WriteAllTextAsync(path, content.ReplaceLineEndings("\n") + "\n", cancellationToken);
        _output.WriteLine($"Created {relativePath}");
    }

    private Command CreateBuildCommand()
    {
        var configOption = new Option<string>("--config", "-c")
        {
            Description = "Path to the configuration file.",
            DefaultValueFactory = _ => ConfigurationLoader.DefaultFileName,
        };
        var outputOption = new Option<string?>("--output", "-o") { Description = "Output directory. Overrides the 'output' configuration value." };
        var basePathOption = new Option<string?>("--base-path") { Description = "URL path where the site is deployed, such as /docs/. Overrides 'site.basePath'." };
        var siteUrlOption = new Option<string?>("--site-url") { Description = "Absolute origin of the site, such as https://example.com. Overrides 'site.url'." };
        var offlineOption = new Option<bool>("--offline") { Description = "Use only cached packages and never contact package sources." };
        var noSourceLinksOption = new Option<bool>("--no-source-links") { Description = "Do not generate links to source files." };
        var warnAsErrorOption = new Option<bool>("--warnaserror") { Description = "Treat warnings as errors." };
        var verbosityOption = new Option<Verbosity>("--verbosity", "-v")
        {
            Description = "Output verbosity: quiet, minimal, normal or detailed.",
            DefaultValueFactory = _ => Verbosity.Normal,
        };
        var watchOption = new Option<bool>("--watch") { Description = "Rebuild the site when the configuration, content or local packages change. Stop with Ctrl+C." };
        var serveOption = new Option<bool>("--serve") { Description = "Serve the site on a local web server to preview it. With --watch, open pages reload after each build. Stop with Ctrl+C." };
        var portOption = new Option<int>("--port")
        {
            Description = "Port of the preview web server. Use 0 to pick a free port.",
            DefaultValueFactory = _ => DefaultPreviewPort,
        };
        portOption.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() is < 0 or > 65535)
            {
                result.AddError("The port must be between 0 and 65535.");
            }
        });

        var command = new Command("build", "Generate the site.")
        {
            configOption,
            outputOption,
            basePathOption,
            siteUrlOption,
            offlineOption,
            noSourceLinksOption,
            warnAsErrorOption,
            verbosityOption,
            watchOption,
            serveOption,
            portOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var verbosity = parseResult.GetValue(verbosityOption);
            var output = parseResult.GetValue(outputOption);
            var options = new BuildOptions
            {
                ConfigurationFile = FullPath.FromPath(parseResult.GetValue(configOption)!),
                OutputDirectory = output is null ? null : FullPath.FromPath(output),
                BasePath = parseResult.GetValue(basePathOption),
                SiteUrl = parseResult.GetValue(siteUrlOption),
                Offline = parseResult.GetValue(offlineOption),
                DisableSourceLinks = parseResult.GetValue(noSourceLinksOption),
                WarningsAsErrors = parseResult.GetValue(warnAsErrorOption),
            };

            var watch = parseResult.GetValue(watchOption);
            var serve = parseResult.GetValue(serveOption);
            if (watch || serve)
                return await RunPreviewAsync(options, verbosity, watch, serve ? parseResult.GetValue(portOption) : null, cancellationToken);

            var result = await BuildAsync(options, verbosity, cancellationToken);
            return GetExitCode(result);
        });

        return command;
    }

    private async Task<BuildResult> BuildAsync(BuildOptions options, Verbosity verbosity, CancellationToken cancellationToken)
    {
        var coordinator = new BuildCoordinator(_stageFactory, new ConsoleBuildReporter(_output, verbosity));
        var result = await coordinator.RunAsync(options, cancellationToken);
        WriteDiagnostics(result, verbosity);
        return result;
    }

    private static int GetExitCode(BuildResult result)
    {
        return result.Status switch
        {
            BuildStatus.Succeeded => ExitCodes.Success,
            BuildStatus.Cancelled => ExitCodes.Cancelled,
            _ => ExitCodes.BuildFailed,
        };
    }

    /// <summary>
    /// Builds the site, then serves it and/or rebuilds it when an input changes, until the user presses Ctrl+C.
    /// Without --watch, a failed build stops immediately. With --watch, failed builds are reported and the next change triggers a new build.
    /// </summary>
    private async Task<int> RunPreviewAsync(BuildOptions options, Verbosity verbosity, bool watch, int? port, CancellationToken cancellationToken)
    {
        using var watcher = watch ? new InputWatcher() : null;
        PreviewServer? server = null;
        try
        {
            var inputs = InputWatcher.GetInputs(options.ConfigurationFile, configuration: null);
            UrlBasePath? servedBasePath = null;
            while (true)
            {
                var result = await BuildAsync(options, verbosity, cancellationToken);
                if (result.Status is BuildStatus.Cancelled || cancellationToken.IsCancellationRequested)
                    return ExitCodes.Success;

                if (!watch && result.Status is not BuildStatus.Succeeded)
                    return GetExitCode(result);

                if (port is { } serverPort && result.Configuration is { } configuration)
                {
                    if (server is null)
                    {
                        try
                        {
                            server = PreviewServer.Start(serverPort, liveReload: watch);
                        }
                        catch (HttpListenerException ex)
                        {
                            _error.WriteLine($"Cannot start the preview server on port {serverPort.ToString(CultureInfo.InvariantCulture)}: {ex.Message}. Use --port to choose another port.");
                            return ExitCodes.BuildFailed;
                        }
                    }

                    server.SetSite(configuration.OutputDirectory, configuration.Site.BasePath);
                    if (servedBasePath != configuration.Site.BasePath)
                    {
                        servedBasePath = configuration.Site.BasePath;
                        _output.WriteLine($"Preview: {server.GetUrl(configuration.Site.BasePath)}");
                    }

                    if (result.Status is BuildStatus.Succeeded && result.IsPublished)
                    {
                        server.NotifyReload();
                    }
                }

                if (watcher is null)
                {
                    _output.WriteLine("Press Ctrl+C to stop.");
                    await WaitForCancellationAsync(cancellationToken);
                    return ExitCodes.Success;
                }

                // Keep watching the previous inputs when the configuration cannot be loaded, so fixing a referenced file triggers a build
                if (result.Configuration is not null)
                {
                    inputs = InputWatcher.GetInputs(options.ConfigurationFile, result.Configuration);
                }

                watcher.Watch(inputs.Files, inputs.Directories);
                _output.WriteLine("Watching for changes. Press Ctrl+C to stop.");
                var changes = await watcher.WaitForChangesAsync(cancellationToken);
                _output.WriteLine($"Change detected: {FormatChanges(changes, result.Configuration)}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        finally
        {
            if (server is not null)
            {
                await server.DisposeAsync();
            }
        }
    }

    private static string FormatChanges(IReadOnlyList<string> changes, SiteConfiguration? configuration)
    {
        const int MaxDisplayed = 3;
        var names = changes.Take(MaxDisplayed).Select(change => configuration?.GetDisplayPath(FullPath.FromPath(change)) ?? Path.GetFileName(change));
        var text = string.Join(", ", names);
        return changes.Count > MaxDisplayed ? text + string.Create(CultureInfo.InvariantCulture, $" and {changes.Count - MaxDisplayed} more") : text;
    }

    private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (cancellationToken.Register(() => completion.TrySetResult()))
        {
            await completion.Task;
        }
    }

    private void WriteDiagnostics(BuildResult result, Verbosity verbosity)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            var visible = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => true,
                DiagnosticSeverity.Warning => verbosity >= Verbosity.Minimal,
                _ => verbosity >= Verbosity.Detailed,
            };

            if (visible)
            {
                var writer = diagnostic.Severity is DiagnosticSeverity.Error ? _error : _output;
                writer.WriteLine(diagnostic.ToString());
            }
        }

        var summary = string.Create(CultureInfo.InvariantCulture, $"{result.ErrorCount} error(s), {result.WarningCount} warning(s)");
        switch (result.Status)
        {
            case BuildStatus.Succeeded when result.IsPublished:
                _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Build succeeded: {result.GeneratedFileCount} file(s) published to '{result.Configuration?.OutputDirectory}'. {summary}"));
                break;
            case BuildStatus.Succeeded:
                _output.WriteLine($"Build succeeded: no site was published. {summary}");
                break;
            case BuildStatus.Cancelled:
                _error.WriteLine($"Build cancelled: the output directory was not modified. {summary}");
                break;
            default:
                _error.WriteLine($"Build failed: the output directory was not modified. {summary}");
                break;
        }
    }
}
