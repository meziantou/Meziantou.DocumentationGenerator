using System.Text;
using Meziantou.DocumentationGenerator.Build;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Tests.Infrastructure;

/// <summary>Runs build stages against a temporary configuration and local packages.</summary>
internal sealed class TestBuild : IAsyncDisposable
{
    private TestBuild(TemporaryDirectory directory, BuildContext context)
    {
        Directory = directory;
        Context = context;
    }

    public TemporaryDirectory Directory { get; }
    public BuildContext Context { get; }
    public FullPath Root => Directory.FullPath;
    public IReadOnlyList<Diagnostic> Diagnostics => Context.Diagnostics.GetDiagnostics();

    /// <summary>Saves the packages in 'packages/' and creates the context. Use <see cref="PackageReference"/> to reference them from the configuration.</summary>
    public static TestBuild Create(string configuration, IEnumerable<TestPackageBuilder> packages, Action<TemporaryDirectory>? setup = null, BuildOptions? options = null)
    {
        var directory = TemporaryDirectory.Create();
        foreach (var package in packages)
        {
            package.Save(directory.FullPath / "packages");
        }

        setup?.Invoke(directory);
        var configurationPath = directory.CreateTextFile("docgen.json", configuration);
        var diagnostics = new DiagnosticBag();
        var buildOptions = options ?? new BuildOptions { ConfigurationFile = configurationPath };
        var siteConfiguration = ConfigurationLoader.Load(Encoding.UTF8.GetBytes(configuration), configurationPath, diagnostics, new ConfigurationOverrides { OutputDirectory = buildOptions.OutputDirectory, BasePath = buildOptions.BasePath, SiteUrl = buildOptions.SiteUrl });
        if (siteConfiguration is null)
            throw new InvalidOperationException("Invalid configuration: " + string.Join('\n', diagnostics.GetDiagnostics()));

        diagnostics.SetSeverityProvider(siteConfiguration.Diagnostics.GetSeverity);
        return new TestBuild(directory, new BuildContext(siteConfiguration, buildOptions, diagnostics, NullBuildReporter.Instance));
    }

    public static string PackageReference(TestPackageBuilder package, string? extra = null)
    {
        var options = extra is null ? "" : ", " + extra;
        return $$"""{ "id": "{{package.Id}}", "version": "{{package.Version}}", "path": "packages/{{$"{package.Id}.{package.Version}.nupkg".ToLowerInvariant()}}"{{options}} }""";
    }

    public async Task<BuildStatus> RunAsync(params IBuildStage[] stages)
    {
        return await BuildCoordinator.RunStagesAsync(Context, stages, TestContext.Current.CancellationToken);
    }

    public async Task RunSuccessfullyAsync(params IBuildStage[] stages)
    {
        var status = await RunAsync(stages);
        if (status != BuildStatus.Succeeded)
            throw new InvalidOperationException($"Build {status}:\n" + string.Join('\n', Diagnostics));
    }

    public ValueTask DisposeAsync() => Directory.DisposeAsync();
}
