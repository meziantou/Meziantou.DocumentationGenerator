using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Api;

namespace Meziantou.DocumentationGenerator.Build;

internal sealed class ApiModelStage : IBuildStage
{
    public string Name => "Read API models";

    public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var reader = new ApiAssemblyReader(context.Diagnostics);
        var result = ImmutableArray.CreateBuilder<ApiAssemblyModel>();
        foreach (var package in context.DiscoveredPackages.Concat(context.DiscoveredReferences))
        {
            result.AddRange(reader.Read(package, cancellationToken));
        }

        context.ApiAssemblies = result.ToImmutable();
        return Task.CompletedTask;
    }
}
