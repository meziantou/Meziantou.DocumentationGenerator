using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>Every aggregated assembly of the build: published packages and reference packages.</summary>
internal sealed class ApiCatalog
{
    public static ApiCatalog Empty { get; } = new([], []);

    private readonly Dictionary<string, List<ApiSymbolModel>> _symbolsByDocumentationId;
    private readonly Dictionary<string, List<ApiSymbolModel>> _symbolsByNameKey;

    public ApiCatalog(ImmutableArray<ApiAssemblyGroup> assemblies, ImmutableArray<ApiPackageModel> packages)
    {
        Assemblies = assemblies;
        Packages = packages;
        _symbolsByDocumentationId = new Dictionary<string, List<ApiSymbolModel>>(StringComparer.Ordinal);
        _symbolsByNameKey = new Dictionary<string, List<ApiSymbolModel>>(StringComparer.Ordinal);
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
    /// <summary>Published packages with an API, in configuration order.</summary>
    public ImmutableArray<ApiPackageModel> Packages { get; }

    /// <summary>Returns the symbols with the given documentation ID in every assembly, in assembly order.</summary>
    public IReadOnlyList<ApiSymbolModel> FindByDocumentationId(string documentationId)
    {
        return _symbolsByDocumentationId.TryGetValue(documentationId, out var symbols) ? symbols : [];
    }

    /// <summary>Returns the members whose documentation ID without parameters and method arity matches, such as every overload of <c>M:Contoso.Client.Send</c>.</summary>
    public IReadOnlyList<ApiSymbolModel> FindByNameKey(string nameKey)
    {
        return _symbolsByNameKey.TryGetValue(nameKey, out var symbols) ? symbols : [];
    }

    public IEnumerable<ApiTypeModel> GetAllTypes() => Assemblies.SelectMany(a => a.GetAllTypes());

    /// <summary><c>M:Contoso.Client.Convert``1(System.Object)</c> becomes <c>M:Contoso.Client.Convert</c>.</summary>
    public static string GetNameKey(string documentationId)
    {
        var end = documentationId.IndexOfAny(['(', '~']);
        var key = end >= 0 ? documentationId[..end] : documentationId;
        var arity = key.IndexOf("``", StringComparison.Ordinal);
        return arity >= 0 ? key[..arity] : key;
    }

    private void Add(ApiSymbolModel symbol)
    {
        AddTo(_symbolsByDocumentationId, symbol.DocumentationId, symbol);
        if (symbol is ApiMemberModel)
        {
            AddTo(_symbolsByNameKey, GetNameKey(symbol.DocumentationId), symbol);
        }

        static void AddTo(Dictionary<string, List<ApiSymbolModel>> dictionary, string key, ApiSymbolModel symbol)
        {
            if (!dictionary.TryGetValue(key, out var list))
            {
                list = [];
                dictionary.Add(key, list);
            }

            list.Add(symbol);
        }
    }
}
