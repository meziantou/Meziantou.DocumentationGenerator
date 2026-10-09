using Meziantou.DocumentationGenerator.Packages;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class AcquisitionStage : IBuildStage
{
    public string Name => "Acquire packages";

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var configuration = context.Configuration;
        if (configuration.Packages.IsEmpty)
            return;

        var acquirer = new PackageAcquirer(configuration.CacheDirectory, configuration.BaseDirectory, context.Options.Offline, context.Diagnostics, context.Reporter.Verbose);
        context.Packages = await acquirer.AcquireAsync(configuration.Packages, cancellationToken);
    }
}
