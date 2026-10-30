using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Discovery;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>The published API of one package: a section of the API reference.</summary>
internal sealed class ApiPackageModel
{
    public required DiscoveredPackage Package { get; init; }
    public required ImmutableArray<ApiAssemblyGroup> Assemblies { get; init; }

    /// <summary>Namespaces sorted ordinally, merging the types of every assembly of the package.</summary>
    public required ImmutableArray<ApiNamespaceModel> Namespaces { get; init; }

    public string Id => Package.Id;

    public string? Description => Package.Package.Metadata.Description;

    public IEnumerable<ApiTypeModel> GetAllTypes() => Assemblies.SelectMany(a => a.GetAllTypes());

    public override string ToString() => Id;
}
