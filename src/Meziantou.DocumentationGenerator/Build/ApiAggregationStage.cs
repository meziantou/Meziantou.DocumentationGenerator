using Meziantou.DocumentationGenerator.Api;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class ApiAggregationStage : IBuildStage
{
    public string Name => "Aggregate API models";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var aggregator = new ApiAggregator(new ApiSymbolPolicy(context.Configuration.ApiFilter), context.Diagnostics);
        context.ApiCatalog = aggregator.Aggregate(context.ApiAssemblies, context.DiscoveredPackages);
        return Task.CompletedTask;
    }
}
