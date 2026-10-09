using System.Text.Json;
using System.Text.Json.Serialization;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>
/// Lists the files owned by the generator in the output directory, so that stale generated files can be removed without
/// touching files created by someone else. It contains no timestamp and no machine-specific path.
/// </summary>
internal sealed class OutputManifest
{
    public const string FileName = ".docgen-manifest.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public int Version { get; set; } = 1;

    public string? Generator { get; set; }

    /// <summary>Generated files, relative to the output directory, sorted ordinally.</summary>
    public List<string> Files { get; set; } = [];

    /// <summary>Packages used to generate the site, for reproducibility.</summary>
    public List<ManifestInput> Inputs { get; set; } = [];

    public static OutputManifest? TryRead(FullPath path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<OutputManifest>(File.ReadAllText(path), SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions).ReplaceLineEndings("\n") + "\n";
}
