using System.Buffers;
using System.Collections.Frozen;
using System.Net;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>
/// Custom elements that authored Markdown can use, replaced when the site is generated:
/// <c>&lt;InstallNuGetPackage Id="..." /&gt;</c>, <c>&lt;InstallDotnetTool Id="..." /&gt;</c> and <c>&lt;NuGetReference Id="..." /&gt;</c>.
/// Element and attribute names are case-insensitive, like HTML. The Markdown parser recognizes them through
/// <see cref="MarkdownComponentExtension"/>.
/// </summary>
internal static class MarkdownComponents
{
    private static readonly SearchValues<char> NameTerminators = SearchValues.Create(" \t\r\n\f=/>");
    private static readonly SearchValues<char> UnquotedValueTerminators = SearchValues.Create(" \t\r\n\f>");

    private static readonly FrozenDictionary<string, Definition> Definitions = new Definition[]
    {
        new("InstallNuGetPackage", ["Id", "Ids", "Version"], (ids, version, _) =>
            new MarkdownComponent(MarkdownComponentKind.Command, string.Join("\n", ids.Select(id => $"dotnet package add {id}{FormatVersionOption(version)}")))),
        new("InstallDotnetTool", ["Id", "Version", "Local"], (ids, version, attributes) => CreateInstallDotnetTool(ids[0], version, attributes)),
        new("NuGetReference", ["Id", "Version"], (ids, version, _) =>
            new MarkdownComponent(MarkdownComponentKind.PackageLink, ids[0], "https://www.nuget.org/packages/" + Uri.EscapeDataString(ids[0]) + (version is null ? "" : "/" + Uri.EscapeDataString(version.ToNormalizedString())))),
    }.ToFrozenDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="tag"/>, an HTML open tag already validated by the Markdown
    /// parser, is a component. Then either <paramref name="component"/> is set, or <paramref name="error"/> explains why
    /// the element is invalid.
    /// </summary>
    public static bool TryCreate(string tag, out MarkdownComponent? component, out string? error)
    {
        component = null;
        error = null;
        var rest = tag.AsSpan();
        if (rest.Length < 2 || rest[0] != '<' || !char.IsAsciiLetter(rest[1]))
            return false;

        rest = rest[1..];
        var nameEnd = rest.IndexOfAny(NameTerminators);
        if (nameEnd < 0 || !Definitions.TryGetValue(rest[..nameEnd].ToString(), out var definition))
            return false;

        rest = rest[nameEnd..];
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            rest = rest.TrimStart();
            if (rest.IsEmpty || rest[0] is '/' or '>')
                break;

            var attributeEnd = rest.IndexOfAny(NameTerminators);
            if (attributeEnd <= 0)
                break;

            var name = rest[..attributeEnd].ToString();
            rest = rest[attributeEnd..].TrimStart();
            var value = "true";
            if (!rest.IsEmpty && rest[0] == '=')
            {
                rest = rest[1..].TrimStart();
                if (!rest.IsEmpty && rest[0] is '"' or '\'')
                {
                    var length = rest[1..].IndexOf(rest[0]);
                    if (length < 0)
                        break;

                    value = rest.Slice(1, length).ToString();
                    rest = rest[(length + 2)..];
                }
                else
                {
                    var valueEnd = rest.IndexOfAny(UnquotedValueTerminators);
                    if (valueEnd < 0)
                    {
                        valueEnd = rest.Length;
                    }

                    value = rest[..valueEnd].ToString();
                    rest = rest[valueEnd..];
                }
            }

            if (!definition.Attributes.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                error = $"<{definition.Name}> does not support the attribute '{name}'; supported attributes: {string.Join(", ", definition.Attributes)}";
                return true;
            }

            if (!attributes.TryAdd(name, WebUtility.HtmlDecode(value).Trim()))
            {
                error = $"<{definition.Name}> declares the attribute '{name}' more than once";
                return true;
            }
        }

        string[] ids;
        if (attributes.TryGetValue("Ids", out var idsText))
        {
            if (attributes.ContainsKey("Id"))
            {
                error = $"<{definition.Name}> cannot declare both 'Id' and 'Ids'";
                return true;
            }

            ids = idsText.Split(',', StringSplitOptions.TrimEntries);
        }
        else
        {
            if (!attributes.TryGetValue("Id", out var id) || id.Length == 0)
            {
                error = $"<{definition.Name}> requires the 'Id' attribute";
                return true;
            }

            ids = [id];
        }

        foreach (var id in ids)
        {
            if (!PackageIdValidator.IsValidPackageId(id))
            {
                error = $"<{definition.Name}> has an invalid package identifier '{id}'";
                return true;
            }
        }

        NuGetVersion? version = null;
        if (attributes.TryGetValue("Version", out var versionText) && !NuGetVersion.TryParse(versionText, out version))
        {
            error = $"<{definition.Name}> has an invalid version '{versionText}'";
            return true;
        }

        component = definition.Create(ids, version, attributes);
        if (component is null)
        {
            error = $"<{definition.Name}> has an invalid attribute value";
        }

        return true;
    }

    private static MarkdownComponent? CreateInstallDotnetTool(string id, NuGetVersion? version, Dictionary<string, string> attributes)
    {
        var local = false;
        if (attributes.TryGetValue("Local", out var localText) && !bool.TryParse(localText, out local))
            return null;

        return new MarkdownComponent(MarkdownComponentKind.Command, $"dotnet tool install {(local ? "" : "--global ")}{id}{FormatVersionOption(version)}");
    }

    private static string FormatVersionOption(NuGetVersion? version) => version is null ? "" : " --version " + version.ToNormalizedString();

    private sealed record Definition(string Name, string[] Attributes, Func<string[], NuGetVersion?, Dictionary<string, string>, MarkdownComponent?> Create);
}
