using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

internal sealed class ApiMemberModel : ApiSymbolModel
{
    public required ApiTypeModel DeclaringType { get; init; }
    public required ApiMemberCategory Category { get; init; }

    private static readonly PublicApiFormattingOptions ReceiverOptions = new() { QualifyTypeNames = false, IncludeAttributes = false };

    /// <summary>
    /// Name used to group overloads: <c>#ctor</c> for constructors, the receiver type and the name for members of C# 14
    /// extension blocks (<c>List&lt;T&gt;.Map</c>), otherwise the member name.
    /// </summary>
    public string OverloadName => Category is ApiMemberCategory.Constructor ? "#ctor" : ExtensionReceiverKey is { } receiver ? receiver + "." + Name : Name;

    public PublicApiMember PrimaryMember => (PublicApiMember)PrimarySymbol;

    /// <summary>The C# 14 extension block that declares the member, such as <c>extension(string value)</c>, or <see langword="null"/>.</summary>
    public PublicApiExtensionBlock? ExtensionBlock => PrimaryMember.ExtensionBlock;

    /// <summary>Short name of the receiver type of an extension member, such as <c>Encoding</c> or <c>List&lt;T&gt;</c>, or <see langword="null"/>.</summary>
    public string? ExtensionReceiverDisplayName => field ??= ExtensionBlock is { } block ? PublicApiFormatter.Format(block.Receiver.Type, ReceiverOptions).Text.TrimEnd('?') : null;

    /// <summary><see cref="ExtensionReceiverDisplayName"/> without spaces, usable in anchors: <c>Dictionary&lt;TKey,TValue&gt;</c>.</summary>
    public string? ExtensionReceiverKey => ExtensionReceiverDisplayName?.Replace(" ", "", StringComparison.Ordinal);

    /// <summary>
    /// Documentation IDs of the methods that implement an extension member on its static class, in every target framework,
    /// such as <c>M:Contoso.Extensions.get_Length(System.String)</c> for the extension property <c>Length</c>.
    /// </summary>
    public IEnumerable<string> GetExtensionImplementationIds()
    {
        return Variants
            .SelectMany(v => v.Symbols)
            .SelectMany(GetAccessors)
            .Select(m => m.ExtensionImplementation?.DocumentationId)
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal);

        static IEnumerable<PublicApiMethod> GetAccessors(PublicApiSymbol symbol) => symbol switch
        {
            PublicApiMethod method => [method],
            PublicApiProperty property => new[] { property.GetMethod, property.SetMethod }.OfType<PublicApiMethod>(),
            PublicApiEvent @event => new[] { @event.AddMethod, @event.RemoveMethod }.OfType<PublicApiMethod>(),
            _ => [],
        };
    }

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
