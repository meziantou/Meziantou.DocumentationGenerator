using System.Collections.Immutable;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

internal sealed class ApiTypeModel : ApiSymbolModel
{
    public required string Namespace { get; init; }

    /// <summary>Name with declaring types and generic parameters, such as <c>Tree&lt;T&gt;.Node</c>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Metadata-style name with declaring types, such as <c>Tree`1.Node</c>, used to build routes.</summary>
    public required string MetadataName { get; init; }
    public ApiTypeModel? DeclaringType { get; init; }
    public ImmutableArray<ApiMemberModel> Members { get; internal set; } = [];
    public ImmutableArray<ApiTypeModel> NestedTypes { get; internal set; } = [];

    public PublicApiType PrimaryType => (PublicApiType)PrimarySymbol;

    public string QualifiedDisplayName => string.IsNullOrEmpty(Namespace) ? DisplayName : Namespace + "." + DisplayName;
}
