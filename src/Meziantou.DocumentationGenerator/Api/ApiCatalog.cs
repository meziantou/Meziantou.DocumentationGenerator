using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>Every aggregated assembly of the build: published products and reference packages.</summary>
internal sealed class ApiCatalog
{
    public static ApiCatalog Empty { get; } = new([], []);

    private readonly Dictionary<string, List<ApiSymbolModel>> _symbolsByDocumentationId;

    public ApiCatalog(ImmutableArray<ApiAssemblyGroup> assemblies, ImmutableArray<ApiProductModel> products)
    {
        Assemblies = assemblies;
        Products = products;
        _symbolsByDocumentationId = new Dictionary<string, List<ApiSymbolModel>>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetAllTypes())
            {
                Add(type);
                foreach (var member in type.Members)
                {
                    Add(member);
                }
            }
        }
    }

    public ImmutableArray<ApiAssemblyGroup> Assemblies { get; }
    public ImmutableArray<ApiProductModel> Products { get; }

    /// <summary>Returns the symbols with the given documentation ID in every assembly, in assembly order.</summary>
    public IReadOnlyList<ApiSymbolModel> FindByDocumentationId(string documentationId)
    {
        return _symbolsByDocumentationId.TryGetValue(documentationId, out var symbols) ? symbols : [];
    }

    public IEnumerable<ApiTypeModel> GetAllTypes() => Assemblies.SelectMany(a => a.GetAllTypes());

    private void Add(ApiSymbolModel symbol)
    {
        if (!_symbolsByDocumentationId.TryGetValue(symbol.DocumentationId, out var list))
        {
            list = [];
            _symbolsByDocumentationId.Add(symbol.DocumentationId, list);
        }

        list.Add(symbol);
    }
}
