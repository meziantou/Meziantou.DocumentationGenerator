namespace Meziantou.DocumentationGenerator.XmlDocs;

/// <summary>
/// Documentation IDs of C# 14 extension blocks. The compiler declares each extension member in an unspeakable grouping
/// type nested in the static class (<c>M:Contoso.Extensions.&lt;G&gt;$9F2C.Run(System.Int32)</c>), and documents the receiver
/// and the type parameters of the block on a marker type nested in the grouping type (<c>T:Contoso.Extensions.&lt;G&gt;$9F2C.&lt;M&gt;$1A3B</c>).
/// PublicApiGenerator exposes these members with their compiler IDs (see <c>PublicApiType.ExtensionBlocks</c>).
/// </summary>
internal static class ExtensionBlockIds
{
    private const string GroupingTypeMarker = ".<G>$";

    /// <summary><see langword="true"/> for the compiler-generated grouping and marker types and their members.</summary>
    public static bool IsCompilerGenerated(string documentationId) => documentationId.Contains(GroupingTypeMarker, StringComparison.Ordinal);
}
