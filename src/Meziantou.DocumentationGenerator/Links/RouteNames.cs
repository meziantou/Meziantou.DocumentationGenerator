using System.Security.Cryptography;
using System.Text;
using Meziantou.DocumentationGenerator.Api;

namespace Meziantou.DocumentationGenerator.Links;

/// <summary>Route and anchor naming rules from docs/architecture.md.</summary>
internal static class RouteNames
{
    public const string GlobalNamespaceFileName = "_global";

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string GetProductPath(string productId) => productId + "/index.html";

    public static string GetApiReferencePath(string productId) => productId + "/api/index.html";

    public static string GetNamespacePath(string productId, string namespaceName) => productId + "/api/namespaces/" + SanitizeFileName(namespaceName.Length == 0 ? GlobalNamespaceFileName : namespaceName) + ".html";

    /// <summary><c>Contoso.Collections.Tree`1.Node</c> becomes <c>Contoso.Collections.Tree-1.Node</c>.</summary>
    public static string GetTypeFileName(ApiTypeModel type)
    {
        var name = string.IsNullOrEmpty(type.Namespace) ? type.MetadataName : type.Namespace + "." + type.MetadataName;
        return SanitizeFileName(name.Replace('`', '-'));
    }

    public static string GetTypePath(string productId, string fileName) => productId + "/api/" + fileName + ".html";

    /// <summary>The member part of the documentation ID: <c>M:Contoso.Client.Send(System.Int32)</c> becomes <c>Send(System.Int32)</c>.</summary>
    public static string GetMemberAnchor(ApiMemberModel member)
    {
        var typeName = member.DeclaringType.DocumentationId[2..];
        var memberId = member.DocumentationId[2..];
        return memberId.StartsWith(typeName + ".", StringComparison.Ordinal) ? memberId[(typeName.Length + 1)..] : memberId;
    }

    public static string GetOverloadAnchor(string overloadName) => overloadName + "-overloads";

    /// <summary>Stable 8-character hash used to disambiguate colliding routes.</summary>
    public static string GetStableHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash.AsSpan(0, 4));
    }

    /// <summary>Makes a file name safe on every platform. Identifier characters are kept; reserved names and characters are escaped.</summary>
    public static string SanitizeFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or '#' or '%')
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(c);
            }
        }

        var result = builder.ToString().TrimEnd('.', ' ');
        if (result.Length == 0 || result is "." or "..")
        {
            result = "_";
        }

        var stem = result.Split('.')[0];
        if (WindowsReservedNames.Contains(stem))
        {
            result = "_" + result;
        }

        return result;
    }
}
