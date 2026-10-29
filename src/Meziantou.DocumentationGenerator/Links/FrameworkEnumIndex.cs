using System.Collections.Frozen;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meziantou.DocumentationGenerator.Links;

/// <summary>
/// Public enums of the .NET runtime running the tool, such as <c>System.StringComparison</c>. Microsoft Learn documents
/// enum values on the page of their enum, so external links to enum values need to know which fields are enum values.
/// </summary>
internal static class FrameworkEnumIndex
{
    private static readonly Lazy<FrozenSet<string>> Enums = new(Load);

    /// <param name="qualifiedName">Name with namespace and declaring types, such as <c>System.Environment.SpecialFolder</c>.</param>
    public static bool IsEnum(string qualifiedName) => Enums.Value.Contains(qualifiedName);

    private static FrozenSet<string> Load()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var directory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return result.ToFrozenSet(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata)
                    continue;

                var reader = pe.GetMetadataReader();
                foreach (var handle in reader.TypeDefinitions)
                {
                    var type = reader.GetTypeDefinition(handle);
                    if (IsPublic(reader, type) && IsEnum(reader, type))
                    {
                        result.Add(GetQualifiedName(reader, type));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
                // Native or unreadable files of the runtime directory are not .NET assemblies
            }
        }

        return result.ToFrozenSet(StringComparer.Ordinal);
    }

    private static bool IsPublic(MetadataReader reader, TypeDefinition type)
    {
        while (true)
        {
            var visibility = type.Attributes & TypeAttributes.VisibilityMask;
            if (visibility is TypeAttributes.Public)
                return true;

            if (visibility is not TypeAttributes.NestedPublic)
                return false;

            type = reader.GetTypeDefinition(type.GetDeclaringType());
        }
    }

    private static bool IsEnum(MetadataReader reader, TypeDefinition type)
    {
        var baseType = type.BaseType;
        if (baseType.IsNil)
            return false;

        var (ns, name) = baseType.Kind switch
        {
            HandleKind.TypeReference => (reader.GetTypeReference((TypeReferenceHandle)baseType).Namespace, reader.GetTypeReference((TypeReferenceHandle)baseType).Name),
            HandleKind.TypeDefinition => (reader.GetTypeDefinition((TypeDefinitionHandle)baseType).Namespace, reader.GetTypeDefinition((TypeDefinitionHandle)baseType).Name),
            _ => (default, default),
        };

        return !name.IsNil && reader.StringComparer.Equals(ns, "System") && reader.StringComparer.Equals(name, "Enum");
    }

    private static string GetQualifiedName(MetadataReader reader, TypeDefinition type)
    {
        var name = reader.GetString(type.Name);
        var declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
            return GetQualifiedName(reader, reader.GetTypeDefinition(declaringType)) + "." + name;

        var ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
