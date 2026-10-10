using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Api;

/// <param name="Name">Namespace name, empty for the global namespace.</param>
/// <param name="Types">Types of the namespace, including nested types, sorted by display name.</param>
internal sealed record ApiNamespaceModel(string Name, ImmutableArray<ApiTypeModel> Types)
{
    public bool IsGlobal => Name.Length == 0;
}
