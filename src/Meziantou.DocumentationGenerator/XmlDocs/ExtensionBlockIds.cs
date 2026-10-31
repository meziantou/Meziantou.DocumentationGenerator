using Meziantou.Framework.PublicApiGenerator;

namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>
/// Documentation IDs of C# 14 extension blocks. The compiler declares each extension member twice: an implementation
/// method on the static class, which is the public API, and a member of an unspeakable grouping type
/// (<c>Contoso.Extensions.&lt;G&gt;$9F2C</c>) that holds the documentation. The receiver parameter and the type
/// parameters of the block are documented on a marker type nested in the grouping type (<c>&lt;G&gt;$9F2C.&lt;M&gt;$1A3B</c>).
/// The implementation is documented with <c>&lt;inheritdoc cref="M:Contoso.Extensions.&lt;G&gt;$9F2C.Run(System.Int32)"/&gt;</c>.
/// </summary>
internal static class ExtensionBlockIds
{
    private const string GroupingTypeMarker = ".<G>$";
    private const string MarkerTypeMarker = ".<M>$";

    /// <summary><see langword="true"/> when <paramref name="documentationId"/> is a member of an extension block, such as <c>M:Contoso.Extensions.&lt;G&gt;$9F2C.Run(System.Int32)</c>.</summary>
    public static bool IsExtensionBlockMember(string documentationId)
    {
        return documentationId.Length > 2 && documentationId[0] is 'M' or 'P' or 'E' or 'F' && documentationId[1] == ':' && documentationId.Contains(GroupingTypeMarker, StringComparison.Ordinal);
    }

    /// <summary><see langword="true"/> for the compiler-generated grouping and marker types and their members, which are not part of the public API.</summary>
    public static bool IsCompilerGenerated(string documentationId) => documentationId.Contains(GroupingTypeMarker, StringComparison.Ordinal);

    /// <summary>Returns <c>Contoso.Extensions.&lt;G&gt;$9F2C</c> for <c>M:Contoso.Extensions.&lt;G&gt;$9F2C.Run(System.Int32)</c>.</summary>
    public static string? GetGroupingTypeName(string documentationId)
    {
        var start = documentationId.IndexOf(GroupingTypeMarker, StringComparison.Ordinal);
        if (start < 0 || documentationId.Length < 2 || documentationId[1] != ':')
            return null;

        var end = documentationId.IndexOf('.', start + GroupingTypeMarker.Length, StringComparison.Ordinal);
        return end < 0 ? null : documentationId[2..end];
    }

    /// <summary>Returns <c>Contoso.Extensions.&lt;G&gt;$9F2C</c> for the marker type <c>T:Contoso.Extensions.&lt;G&gt;$9F2C.&lt;M&gt;$1A3B</c>, or <see langword="null"/>.</summary>
    public static string? GetGroupingTypeNameOfMarker(string documentationId)
    {
        if (!documentationId.StartsWith("T:", StringComparison.Ordinal))
            return null;

        var index = documentationId.LastIndexOf(MarkerTypeMarker, StringComparison.Ordinal);
        return index < 0 || !documentationId.AsSpan(2, index - 2).Contains(GroupingTypeMarker, StringComparison.Ordinal) ? null : documentationId[2..index];
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="member"/> is the implementation of the extension block member
    /// <paramref name="blockMemberId"/>: both are declared by the same static class and have the same name. Extension
    /// property accessors implement the property.
    /// </summary>
    public static bool IsImplementation(PublicApiMember member, string blockMemberId)
    {
        if (member.DeclaringType is not { } declaringType || !IsExtensionBlockMember(blockMemberId))
            return false;

        var prefix = declaringType.DocumentationId[2..] + GroupingTypeMarker;
        if (string.CompareOrdinal(blockMemberId, 2, prefix, 0, prefix.Length) != 0)
            return false;

        var nameStart = blockMemberId.IndexOf('.', 2 + prefix.Length, StringComparison.Ordinal);
        if (nameStart < 0)
            return false;

        var name = StripArity(blockMemberId[(nameStart + 1)..]);
        var memberName = StripArity(member.Name);
        return blockMemberId[0] switch
        {
            'P' => memberName == "get_" + name || memberName == "set_" + name,
            'E' => memberName == "add_" + name || memberName == "remove_" + name,
            _ => memberName == name,
        };

        static string StripArity(string value)
        {
            var end = value.IndexOfAny(['(', '`', '~']);
            return end < 0 ? value : value[..end];
        }
    }
}
