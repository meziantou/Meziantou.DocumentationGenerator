using System.Text.Json;

namespace Meziantou.DocumentationGenerator.Search;

internal static class SearchIndexReader
{
    /// <summary>Returns the URLs of the items of an index, relative to the base path.</summary>
    public static IEnumerable<string> GetUrls(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.GetProperty("items").EnumerateArray().Select(item => item[1].GetString()!)];
    }
}
