using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.Api;

/// <summary>
/// Visibility and filtering rules from docs/architecture.md, applied on top of the PublicApiGenerator model which already
/// excludes internal and private symbols, unspeakable names and compiler-generated types.
/// </summary>
internal sealed class ApiSymbolPolicy(ApiFilter filter)
{
    public static ApiSymbolPolicy Default { get; } = new(ApiFilter.All);

    public bool IsDocumented(PublicApiType type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (!IsAccessible(current) || IsEmbeddedAttribute(current))
                return false;
        }

        return filter.IsTypeIncluded(type.Namespace ?? "", GetTypeNames(type));
    }

    public bool IsDocumented(PublicApiMember member)
    {
        if (member.DeclaringType is null || !IsDocumented(member.DeclaringType))
            return false;

        // Accessors are documented with their property or event, and the signature of a delegate is its declaration
        if (member is PublicApiMethod { MethodKind: PublicApiMethodKind.PropertyGet or PublicApiMethodKind.PropertySet or PublicApiMethodKind.EventAdd or PublicApiMethodKind.EventRemove or PublicApiMethodKind.EventRaise or PublicApiMethodKind.DelegateInvoke })
            return false;

        if (!IsAccessible(member))
            return false;

        // The equality contract of records is an implementation detail
        if (member is PublicApiProperty { Name: "EqualityContract", IsCompilerGenerated: true })
            return false;

        if (member.Kind is PublicApiSymbolKind.Method && member is PublicApiMethod { MethodKind: PublicApiMethodKind.Destructor })
            return false;

        var kind = member.Kind switch
        {
            PublicApiSymbolKind.Method => 'M',
            PublicApiSymbolKind.Property => 'P',
            PublicApiSymbolKind.Field => 'F',
            PublicApiSymbolKind.Event => 'E',
            _ => 'M',
        };

        return filter.IsMemberIncluded(kind, GetTypeNames(member.DeclaringType)[^1] + "." + member.Name);
    }

    public bool IsDocumented(PublicApiSymbol symbol) => symbol switch
    {
        PublicApiType type => IsDocumented(type),
        PublicApiMember member => IsDocumented(member),
        _ => false,
    };

    /// <summary>
    /// <c>Microsoft.CodeAnalysis.EmbeddedAttribute</c> is compiler infrastructure. Source packages often declare it so their types
    /// can be marked <c>[Embedded]</c>, which would add a <c>Microsoft.CodeAnalysis</c> namespace to the API reference. The types it
    /// marks are still documented: in a source package, they are the package's own API.
    /// </summary>
    private static bool IsEmbeddedAttribute(PublicApiType type)
        => type is { Namespace: "Microsoft.CodeAnalysis", Name: "EmbeddedAttribute", DeclaringType: null };

    /// <summary>Protected members of sealed types cannot be accessed by consumers.</summary>
    private static bool IsAccessible(PublicApiSymbol symbol)
    {
        return symbol.Accessibility switch
        {
            PublicApiAccessibility.Public => true,

            // Only assemblies compiled from source packages are read with internal symbols, which the consuming project can use
            PublicApiAccessibility.Internal => true,
            PublicApiAccessibility.Protected or PublicApiAccessibility.ProtectedInternal => symbol.DeclaringType is not { } declaringType || !(declaringType.IsSealed || declaringType.IsStatic || declaringType.TypeKind is PublicApiTypeKind.Struct),
            _ => symbol is PublicApiMember { IsExplicitInterfaceImplementation: true },
        };
    }

    /// <summary>Qualified names of the type and its declaring types, outermost first, using '.' between nested types.</summary>
    public static IReadOnlyList<string> GetTypeNames(PublicApiType type)
    {
        var types = new List<PublicApiType>();
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            types.Insert(0, current);
        }

        var result = new List<string>();
        var prefix = string.IsNullOrEmpty(types[0].Namespace) ? "" : types[0].Namespace + ".";
        foreach (var current in types)
        {
            prefix += current.Name;
            result.Add(prefix);
            prefix += ".";
        }

        return result;
    }
}
