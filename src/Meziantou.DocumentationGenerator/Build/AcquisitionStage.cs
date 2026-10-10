using Meziantou.DocumentationGenerator.Packages;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class AcquisitionStage : IBuildStage
{
    public string Name => "Acquire packages";

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var configuration = context.Configuration;
        if (configuration.Packages.IsEmpty && configuration.References.IsEmpty)
            return;

        var acquirer = new PackageAcquirer(configuration.CacheDirectory, configuration.BaseDirectory, context.Options.Offline, context.Diagnostics, context.Reporter.Verbose);
        var packages = await acquirer.AcquireAsync([.. configuration.Packages, .. configuration.References], cancellationToken);
        context.Packages = [.. packages.Where(p => !p.Configuration.IsReference)];
        context.References = [.. packages.Where(p => p.Configuration.IsReference)];
    }
}
