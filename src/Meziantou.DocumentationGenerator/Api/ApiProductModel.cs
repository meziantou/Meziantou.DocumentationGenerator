using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Configuration;

namespace Meziantou.DocumentationGenerator.Api;

internal sealed class ApiProductModel
{
    public required ProductConfiguration Product { get; init; }
    public required ImmutableArray<ApiAssemblyGroup> Assemblies { get; init; }

    /// <summary>Namespaces sorted ordinally, merging the types of every assembly of the product.</summary>
    public required ImmutableArray<ApiNamespaceModel> Namespaces { get; init; }

    public IEnumerable<ApiTypeModel> GetAllTypes() => Assemblies.SelectMany(a => a.GetAllTypes());
}
