using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

internal sealed class ApiMemberModel : ApiSymbolModel
{
    public required ApiTypeModel DeclaringType { get; init; }
    public required ApiMemberCategory Category { get; init; }

    /// <summary>Name used to group overloads: <c>#ctor</c> for constructors, otherwise the member name.</summary>
    public string OverloadName => Category is ApiMemberCategory.Constructor ? "#ctor" : Name;

    public PublicApiMember PrimaryMember => (PublicApiMember)PrimarySymbol;

    public static ApiMemberCategory GetCategory(PublicApiMember member, PublicApiType declaringType)
    {
        return member switch
        {
            PublicApiMethod { MethodKind: PublicApiMethodKind.Constructor } => ApiMemberCategory.Constructor,
            PublicApiMethod { MethodKind: PublicApiMethodKind.Operator or PublicApiMethodKind.Conversion } => ApiMemberCategory.Operator,
            PublicApiMethod => ApiMemberCategory.Method,
            PublicApiProperty { IsIndexer: true } => ApiMemberCategory.Indexer,
            PublicApiProperty => ApiMemberCategory.Property,
            PublicApiEvent => ApiMemberCategory.Event,
            PublicApiField when declaringType.TypeKind is PublicApiTypeKind.Enum => ApiMemberCategory.EnumValue,
            PublicApiField { IsConst: true } => ApiMemberCategory.Constant,
            _ => ApiMemberCategory.Field,
        };
    }
}
