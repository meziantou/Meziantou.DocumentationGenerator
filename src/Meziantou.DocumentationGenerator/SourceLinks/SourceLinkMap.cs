using System.Text.Json;

namespace Meziantou.DocumentationGenerator.SourceLinks;

/// <summary>Source Link mapping from local document paths to URLs, as stored in portable PDBs.</summary>
internal sealed class SourceLinkMap
{
    private readonly List<(string Path, bool IsPrefix, string Url)> _entries;

    private SourceLinkMap(List<(string, bool, string)> entries)
    {
        _entries = entries;
    }

    public static SourceLinkMap? TryParse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            if (!document.RootElement.TryGetProperty("documents", out var documents) || documents.ValueKind is not JsonValueKind.Object)
                return null;

            var entries = new List<(string, bool, string)>();
            foreach (var property in documents.EnumerateObject())
            {
                if (property.Value.ValueKind is not JsonValueKind.String)
                    continue;

                var key = property.Name;
                var url = property.Value.GetString()!;
                var isPrefix = key.EndsWith('*', StringComparison.Ordinal);
                if (isPrefix != url.EndsWith('*', StringComparison.Ordinal))
                    continue;

                entries.Add((isPrefix ? key[..^1] : key, isPrefix, isPrefix ? url[..^1] : url));
            }

            // The longest matching path wins
            entries.Sort((a, b) => b.Item1.Length.CompareTo(a.Item1.Length));
            return new SourceLinkMap(entries);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Returns the URL of a document, or <see langword="null"/> when the document is not mapped (for instance generated code).</summary>
    public string? GetUrl(string documentPath)
    {
        foreach (var (path, isPrefix, url) in _entries)
        {
            if (isPrefix)
            {
                if (documentPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                    return url + Escape(documentPath[path.Length..].Replace('\\', '/'));
            }
            else if (string.Equals(documentPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }
        }

        return null;
    }

    private static string Escape(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
