using System.Diagnostics;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Diagnostics;

namespace Meziantou.DocumentationGenerator.Build;

/// <summary>
/// Loads the configuration and runs the build stages in order. The build stops at the first stage that reports an error,
/// so publication never runs after a fatal diagnostic, a cancellation or an unexpected exception.
/// </summary>
internal sealed class BuildCoordinator(Func<BuildContext, IReadOnlyList<IBuildStage>> stageFactory, IBuildReporter reporter)
{
    public async Task<BuildResult> RunAsync(BuildOptions options, CancellationToken cancellationToken)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetSeverityProvider(descriptor => options.WarningsAsErrors && descriptor.DefaultSeverity is DiagnosticSeverity.Warning ? DiagnosticSeverity.Error : descriptor.DefaultSeverity);

        reporter.StageStarted("Configuration");
        var configuration = ConfigurationLoader.Load(options.ConfigurationFile, diagnostics, CreateOverrides(options));
        if (configuration is null)
            return new BuildResult(BuildStatus.Failed, diagnostics.GetDiagnostics(), Configuration: null, IsPublished: false, GeneratedFileCount: 0);

        var diagnosticOptions = configuration.Diagnostics;
        diagnostics.SetSeverityProvider(descriptor =>
        {
            var severity = diagnosticOptions.GetSeverity(descriptor);
            return options.WarningsAsErrors && severity is DiagnosticSeverity.Warning ? DiagnosticSeverity.Error : severity;
        });
        if (diagnostics.HasErrors)
            return new BuildResult(BuildStatus.Failed, diagnostics.GetDiagnostics(), configuration, IsPublished: false, GeneratedFileCount: 0);

        var context = new BuildContext(configuration, options, diagnostics, reporter);
        BuildStatus status;
        try
        {
            status = await RunStagesAsync(context, stageFactory(context), cancellationToken);
        }
        finally
        {
            context.RunCleanups();
        }

        return new BuildResult(status, diagnostics.GetDiagnostics(), configuration, context.IsPublished, context.GeneratedFileCount);
    }

    public static async Task<BuildStatus> RunStagesAsync(BuildContext context, IReadOnlyList<IBuildStage> stages, CancellationToken cancellationToken)
    {
        foreach (var stage in stages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                context.Diagnostics.Report(DiagnosticDescriptors.BuildCancelled, $"The build was cancelled before the '{stage.Name}' stage; the output directory was not modified");
                return BuildStatus.Cancelled;
            }

            context.Reporter.StageStarted(stage.Name);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await stage.ExecuteAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                context.Diagnostics.Report(DiagnosticDescriptors.BuildCancelled, $"The build was cancelled during the '{stage.Name}' stage; the output directory was not modified");
                return BuildStatus.Cancelled;
            }
#pragma warning disable CA1031 // Unexpected failures of a stage are reported as diagnostics
            catch (Exception ex)
#pragma warning restore CA1031
            {
                context.Diagnostics.Report(DiagnosticDescriptors.StageFailed, $"The '{stage.Name}' stage failed: {ex.GetType().Name}: {ex.Message}");
                context.Reporter.Verbose(ex.ToString());
                return BuildStatus.Failed;
            }

            context.Reporter.StageCompleted(stage.Name, stopwatch.Elapsed);
            if (context.Diagnostics.HasErrors)
                return BuildStatus.Failed;
        }

        return BuildStatus.Succeeded;
    }

    private static ConfigurationOverrides CreateOverrides(BuildOptions options)
    {
        return new ConfigurationOverrides
        {
            OutputDirectory = options.OutputDirectory,
            BasePath = options.BasePath,
            SiteUrl = options.SiteUrl,
            DisableSourceLinks = options.DisableSourceLinks,
        };
    }
}
