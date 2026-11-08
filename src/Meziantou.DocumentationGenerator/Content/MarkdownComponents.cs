using System.Buffers;
using System.Collections.Frozen;
using System.Net;
using Meziantou.DocumentationGenerator.DependencyGraphs;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>
/// Custom elements that authored Markdown can use, replaced when the site is generated:
/// <c>&lt;InstallNuGetPackage Id="..." /&gt;</c>, <c>&lt;InstallDotnetTool Id="..." /&gt;</c>, <c>&lt;NuGetReference Id="..." /&gt;</c>
/// and <c>&lt;DependencyGraph Packages="*" /&gt;</c>.
/// Element and attribute names are case-insensitive, like HTML. The Markdown parser recognizes them through
/// <see cref="MarkdownComponentExtension"/>.
/// </summary>
internal static class MarkdownComponents
{
    private static readonly SearchValues<char> NameTerminators = SearchValues.Create(" \t\r\n\f=/>");
    private static readonly SearchValues<char> UnquotedValueTerminators = SearchValues.Create(" \t\r\n\f>");

    private static readonly FrozenDictionary<string, Definition> Definitions = new Definition[]
    {
        new("InstallNuGetPackage", ["Id", "Version"], attributes => CreateFromPackageIds("InstallNuGetPackage", attributes, allowCollection: true, (ids, version) =>
            new MarkdownComponent(MarkdownComponentKind.Command, string.Join("\n", ids.Select(id => $"dotnet package add {id}{FormatVersionOption(version)}"))))),
        new("InstallDotnetTool", ["Id", "Version", "Local"], attributes => CreateFromPackageIds("InstallDotnetTool", attributes, allowCollection: false, (ids, version) => CreateInstallDotnetTool(ids[0], version, attributes))),
        new("NuGetReference", ["Id", "Version"], attributes => CreateFromPackageIds("NuGetReference", attributes, allowCollection: false, (ids, version) =>
            new MarkdownComponent(MarkdownComponentKind.PackageLink, ids[0], Links.LinkService.GetNuGetOrgPackageUrl(ids[0], version?.ToNormalizedString())))),
        new("DependencyGraph", ["Packages", "External", "Direction"], CreateDependencyGraph, BlockOnly: true),
    }.ToFrozenDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="tag"/>, an HTML open tag already validated by the Markdown
    /// parser, is a component. Then either <paramref name="component"/> is set, or <paramref name="error"/> explains why
    /// the element is invalid. <paramref name="inline"/> is <see langword="true"/> for an element inside a paragraph.
    /// </summary>
    public static bool TryCreate(string tag, bool inline, out MarkdownComponent? component, out string? error)
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

        if (inline && definition.BlockOnly)
        {
            error = $"<{definition.Name}> must be on its own line, separated from other text by a blank line";
            return true;
        }

        (component, error) = definition.Create(attributes);
        if (component is null)
        {
            error ??= $"<{definition.Name}> has an invalid attribute value";
        }

        return true;
    }

    private static (MarkdownComponent? Component, string? Error) CreateFromPackageIds(string name, Dictionary<string, string> attributes, bool allowCollection, Func<string[], NuGetVersion?, MarkdownComponent?> create)
    {
        if (!attributes.TryGetValue("Id", out var idText) || idText.Length == 0)
            return (null, $"<{name}> requires the 'Id' attribute");

        string[] ids = allowCollection ? idText.Split(',', StringSplitOptions.TrimEntries) : [idText];
        foreach (var id in ids)
        {
            if (!PackageIdValidator.IsValidPackageId(id))
                return (null, $"<{name}> has an invalid package identifier '{id}'");
        }

        NuGetVersion? version = null;
        if (attributes.TryGetValue("Version", out var versionText) && !NuGetVersion.TryParse(versionText, out version))
            return (null, $"<{name}> has an invalid version '{versionText}'");

        return (create(ids, version), null);
    }

    private static (MarkdownComponent? Component, string? Error) CreateDependencyGraph(Dictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue("Packages", out var packagesText) || packagesText.Length == 0)
            return (null, "<DependencyGraph> requires the 'Packages' attribute");

        var patterns = packagesText.Split(',', StringSplitOptions.TrimEntries);
        foreach (var pattern in patterns)
        {
            // A pattern is a package identifier in which '*' matches any characters
            if (pattern.Length == 0 || !PackageIdValidator.IsValidPackageId(pattern.Replace('*', 'a')))
                return (null, $"<DependencyGraph> has an invalid package pattern '{pattern}'");
        }

        var external = false;
        if (attributes.TryGetValue("External", out var externalText) && !bool.TryParse(externalText, out external))
            return (null, $"<DependencyGraph> has an invalid 'External' value '{externalText}'; expected true or false");

        var direction = DependencyGraphDirection.LeftRight;
        if (attributes.TryGetValue("Direction", out var directionText))
        {
            if (string.Equals(directionText, "TopDown", StringComparison.OrdinalIgnoreCase))
            {
                direction = DependencyGraphDirection.TopDown;
            }
            else if (!string.Equals(directionText, "LeftRight", StringComparison.OrdinalIgnoreCase))
            {
                return (null, $"<DependencyGraph> has an invalid 'Direction' value '{directionText}'; expected LeftRight or TopDown");
            }
        }

        var request = new DependencyGraphRequest([.. patterns.Distinct(StringComparer.OrdinalIgnoreCase)], external, direction);
        return (new MarkdownComponent(MarkdownComponentKind.DependencyGraph, packagesText) { Graph = request }, null);
    }

    private static MarkdownComponent? CreateInstallDotnetTool(string id, NuGetVersion? version, Dictionary<string, string> attributes)
    {
        var local = false;
        if (attributes.TryGetValue("Local", out var localText) && !bool.TryParse(localText, out local))
            return null;

        return new MarkdownComponent(MarkdownComponentKind.Command, $"dotnet tool install {(local ? "" : "--global ")}{id}{FormatVersionOption(version)}");
    }

    private static string FormatVersionOption(NuGetVersion? version) => version is null ? "" : " --version " + version.ToNormalizedString();

    private sealed record Definition(string Name, string[] Attributes, Func<Dictionary<string, string>, (MarkdownComponent? Component, string? Error)> Create, bool BlockOnly = false);
}
