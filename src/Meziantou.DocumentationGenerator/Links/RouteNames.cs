using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.DocumentationGenerator.Api;

namespace Meziantou.DocumentationGenerator.Links;

/// <summary>Route and anchor naming rules from docs/architecture.md.</summary>
internal static partial class RouteNames
{
    public const string GlobalNamespaceFileName = "_global";

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>URL segment of a package: its lowercase identifier, <c>Contoso.Json</c> becomes <c>contoso.json</c>.</summary>
    public static string GetPackageSlug(string packageId) => SanitizeFileName(packageId.ToLowerInvariant());

    public static string GetPackagePath(string packageId) => "api/" + GetPackageSlug(packageId) + "/index.html";

    public static string GetNamespacePath(string packageId, string namespaceName) => "api/" + GetPackageSlug(packageId) + "/namespaces/" + SanitizeFileName(namespaceName.Length == 0 ? GlobalNamespaceFileName : namespaceName) + ".html";

    /// <summary><c>Contoso.Collections.Tree`1.Node</c> becomes <c>Contoso.Collections.Tree-1.Node</c>.</summary>
    public static string GetTypeFileName(ApiTypeModel type)
    {
        var name = string.IsNullOrEmpty(type.Namespace) ? type.MetadataName : type.Namespace + "." + type.MetadataName;
        return SanitizeFileName(name.Replace('`', '-'));
    }

    public static string GetTypePath(string packageId, string fileName) => "api/" + GetPackageSlug(packageId) + "/" + fileName + ".html";

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

    /// <summary>URL segment computed from a title: <c>Loading and saving</c> becomes <c>loading-and-saving</c>. Accents are removed; other characters become <c>-</c>.</summary>
    public static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;

            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>Removes the number prefix that orders files and folders: <c>02-GettingStarted</c> becomes <c>GettingStarted</c>.</summary>
    public static string RemoveOrderPrefix(string name)
    {
        var result = OrderPrefixRegex().Replace(name, "");
        return result.Length == 0 ? name : result;
    }

    [GeneratedRegex(@"^[0-9]+[-_.\s]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OrderPrefixRegex();
}
