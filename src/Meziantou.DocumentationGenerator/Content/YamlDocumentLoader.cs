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
            diagnostics.Report(descriptor, $"Invalid YAML: {ex.Message}", new DiagnosticLocation { FilePath = displayPath, Line = ex.Start.Line + lineOffset, Column = ex.Start.Column });
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

        return mark is { } m && m.Line > 0 ? (m.Line, m.Column) : null;
    }

    public static string? GetString(YamlMapping mapping, string key)
    {
        return mapping.TryGetValue(key, out var value) && value is YamlValue scalar ? scalar.Value : null;
    }
}
