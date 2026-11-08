using System.Globalization;
using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Loads YAML from authored files with bounded nesting and no alias expansion.</summary>
internal static class YamlDocumentLoader
{
    public const int MaxSize = 1024 * 1024;

    private static readonly YamlSerializerOptions Options = new()
    {
        MaxDepth = 32,
        AllowAnchors = false,
        AllowAliases = false,
    };

    public static YamlElement? Load(string text, string displayPath, DiagnosticBag diagnostics, DiagnosticDescriptor descriptor, int lineOffset = 0)
    {
        if (text.Length > MaxSize)
        {
            diagnostics.Report(descriptor, $"The YAML document is too large ({text.Length} characters)", new DiagnosticLocation { FilePath = displayPath });
            return null;
        }

        try
        {
            var stream = YamlStream.Load(new StringReader(text), Options);
            return stream.Count == 0 ? null : stream[0].Contents;
        }
        catch (YamlException ex)
        {
            diagnostics.Report(descriptor, $"Invalid YAML: {ex.Message}", new DiagnosticLocation { FilePath = displayPath, Line = ex.Start.Line + 1 + lineOffset, Column = ex.Start.Column + 1 });
            return null;
        }
    }

    public static (int Line, int Column)? GetLocation(YamlElement element)
    {
        var mark = element switch
        {
            YamlValue value => value.Scalar?.Start,
            YamlMapping mapping => mapping.MappingStart?.Start,
            YamlSequence sequence => sequence.SequenceStart?.Start,
            _ => null,
        };

        // Marks are zero-based
        return mark is { } m && m.Line >= 0 ? (m.Line + 1, m.Column + 1) : null;
    }

    public static string? GetString(YamlMapping mapping, string key)
    {
        return mapping.TryGetValue(key, out var value) && value is YamlValue scalar ? scalar.Value : null;
    }

    /// <summary>Reads the optional integer <c>sortOrder</c> key. A missing or invalid value is 0; an invalid value is reported.</summary>
    public static int GetSortOrder(YamlMapping mapping, string displayPath, DiagnosticBag diagnostics, DiagnosticDescriptor descriptor, int lineOffset = 0)
    {
        if (!mapping.TryGetValue("sortOrder", out var value))
            return 0;

        if (value is YamlValue { Value: { } text } && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result))
            return result;

        var location = GetLocation(value);
        diagnostics.Report(descriptor, "'sortOrder' must be an integer; the default value 0 is used", new DiagnosticLocation
        {
            FilePath = displayPath,
            Line = location is { } l ? l.Line + lineOffset : lineOffset > 0 ? lineOffset : null,
            Column = location?.Column,
        });
        return 0;
    }
}
