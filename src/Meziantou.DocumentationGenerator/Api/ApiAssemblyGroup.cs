using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Discovery;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>The builds of one assembly of one package for every documented target framework.</summary>
internal sealed class ApiAssemblyGroup
{
    public required DiscoveredPackage Package { get; init; }
    public required string AssemblyName { get; init; }
    public string? ProductId { get; init; }

    /// <summary>Models per target framework, in <see cref="TargetFrameworks"/> order.</summary>
    public required ImmutableArray<ApiAssemblyModel> Models { get; init; }
    public required PublicApiAggregatedAssembly Aggregate { get; init; }
    public required ImmutableArray<string> TargetFrameworks { get; init; }
    public ImmutableArray<ApiTypeModel> Types { get; internal set; } = [];

    public string Scope => ApiAssemblyModel.CreateScope(Package.Id, AssemblyName);
    public bool IsReference => Package.Package.Configuration.IsReference;

    public ApiAssemblyModel GetModel(string targetFramework) => Models.Single(m => m.TargetFramework == targetFramework);

    public IEnumerable<ApiTypeModel> GetAllTypes()
    {
        foreach (var type in Types)
        {
            foreach (var nested in Flatten(type))
            {
                yield return nested;
            }
        }

        static IEnumerable<ApiTypeModel> Flatten(ApiTypeModel type)
        {
            yield return type;
            foreach (var nested in type.NestedTypes)
            {
                foreach (var t in Flatten(nested))
                {
                    yield return t;
                }
            }
        }
    }

    public override string ToString() => Scope;
}
