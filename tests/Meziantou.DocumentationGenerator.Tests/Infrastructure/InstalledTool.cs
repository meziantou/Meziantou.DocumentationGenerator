namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Packs the tool and installs it in a temporary local tool manifest, isolated from the global NuGet cache.</summary>
internal sealed class InstalledTool
{
    public const string Version = "0.0.0-smoketest";

    private readonly string _rootDirectory;
    private readonly Dictionary<string, string?> _environment;

    private InstalledTool(string rootDirectory, Dictionary<string, string?> environment)
    {
        _rootDirectory = rootDirectory;
        _environment = environment;
    }

    public static async Task<InstalledTool> CreateAsync(string rootDirectory, CancellationToken cancellationToken)
    {
        var packageDirectory = Path.Combine(rootDirectory, "packages");
        var toolDirectory = Path.Combine(rootDirectory, "tool");
        Directory.CreateDirectory(toolDirectory);

        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["NUGET_PACKAGES"] = Path.Combine(rootDirectory, "nuget-cache"),

            // The local tool resolver cache is stored in the CLI home and keyed by package version only
            ["DOTNET_CLI_HOME"] = Path.Combine(rootDirectory, "cli-home"),
        };

        var packageFromEnvironment = Environment.GetEnvironmentVariable("DOCGEN_TOOL_PACKAGE_DIRECTORY");
        string version;
        if (!string.IsNullOrEmpty(packageFromEnvironment))
        {
            // CI provides the package produced by the pack step, so the test validates the bits that are published
            packageDirectory = packageFromEnvironment;
            var package = Directory.GetFiles(packageDirectory, "Meziantou.DocumentationGenerator.*.nupkg").Single();
            version = Path.GetFileNameWithoutExtension(package)["Meziantou.DocumentationGenerator.".Length..];
        }
        else
        {
            version = Version;
            var pack = await ProcessRunner.RunAsync(
                ProcessRunner.DotnetPath,
                ["pack", RepositoryPaths.ToolProject, "--output", packageDirectory, "-p:Version=" + version, "--disable-build-servers"],
                RepositoryPaths.Root,
                environment: null,
                cancellationToken);
            if (pack.ExitCode != 0)
                throw new InvalidOperationException("dotnet pack failed:\n" + pack.AllOutput);
        }

        await File.WriteAllTextAsync(Path.Combine(toolDirectory, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{packageDirectory}" />
              </packageSources>
            </configuration>
            """, cancellationToken);

        await RunCheckedAsync(["new", "tool-manifest"], toolDirectory, environment, cancellationToken);
        await RunCheckedAsync(["tool", "install", "--local", "Meziantou.DocumentationGenerator", "--version", version], toolDirectory, environment, cancellationToken);
        return new InstalledTool(toolDirectory, environment);
    }

    public string RootDirectory => _rootDirectory;

    /// <summary>Runs the installed tool. 'dotnet tool run' finds the manifest from the working directory, so paths in arguments must be absolute.</summary>
    public Task<ProcessResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken) => RunAsync(arguments, workingDirectory: null, environment: null, cancellationToken);

    /// <param name="workingDirectory">A directory inside <see cref="RootDirectory"/>, so that the tool manifest is found.</param>
    public Task<ProcessResult> RunAsync(IEnumerable<string> arguments, string? workingDirectory, IReadOnlyDictionary<string, string?>? environment, CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string?>(_environment, StringComparer.Ordinal);
        foreach (var (key, value) in environment ?? new Dictionary<string, string?>())
        {
            variables[key] = value;
        }

        return ProcessRunner.RunAsync(ProcessRunner.DotnetPath, ["tool", "run", "Meziantou.DocumentationGenerator", "--", .. arguments], workingDirectory ?? _rootDirectory, variables, cancellationToken);
    }

    private static async Task RunCheckedAsync(IEnumerable<string> arguments, string workingDirectory, Dictionary<string, string?> environment, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(ProcessRunner.DotnetPath, arguments, workingDirectory, environment, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"dotnet {string.Join(' ', arguments)} failed:\n{result.AllOutput}");
    }
}
