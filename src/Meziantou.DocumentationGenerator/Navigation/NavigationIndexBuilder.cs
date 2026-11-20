using System.Text;
using System.Text.Json;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Navigation;

/// <summary>
/// Builds the navigation index, from which the sidebar loads the children of the sections that a page does not render.
/// <c>nodes[id]</c> lists the children of the collapsible node <c>id</c> (see <see cref="SiteNavigation.CollapsibleNodes"/>).
/// Each child is <c>[title, url, flags]</c>, followed by its own identifier when it is collapsible. The URL is relative to
/// the base path, absolute for external links, and <see langword="null"/> when the node has no page. Flags are a
/// combination of <see cref="GroupFlag"/>, <see cref="ExternalFlag"/> and <see cref="ApiReferenceFlag"/>.
/// </summary>
internal static class NavigationIndexBuilder
{
    public const string FileName = "navigation.json";
    public const int Version = 1;

    public const int GroupFlag = 1;
    public const int ExternalFlag = 2;
    public const int ApiReferenceFlag = 4;

    public static string Build(SiteNavigation navigation, UrlBuilder urls)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("nodes");
            foreach (var node in navigation.CollapsibleNodes)
            {
                writer.WriteStartArray();
                foreach (var child in node.Children)
                {
                    writer.WriteStartArray();
                    writer.WriteStringValue(child.Title);
                    if (GetUrl(child, urls) is { } url)
                    {
                        writer.WriteStringValue(url);
                    }
                    else
                    {
                        writer.WriteNullValue();
                    }

                    writer.WriteNumberValue((child.IsGroup ? GroupFlag : 0) | (child.IsExternal ? ExternalFlag : 0) | (child.Kind == NavigationNode.ApiReferenceKind ? ApiReferenceFlag : 0));
                    if (SiteNavigation.IsCollapsible(child))
                    {
                        writer.WriteNumberValue(navigation.GetCollapsibleNodeId(child));
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Returns the site URLs of the index, to validate them like the links of a page.</summary>
    public static IEnumerable<string> GetSiteUrls(SiteNavigation navigation) => navigation.CollapsibleNodes.SelectMany(n => n.Children).Where(n => n is { IsExternal: false, Url: not null }).Select(n => n.Url!);

    private static string? GetUrl(NavigationNode node, UrlBuilder urls)
    {
        if (node.Url is not { } url || node.IsExternal || !url.StartsWith(urls.BasePath.Value, StringComparison.Ordinal))
            return node.Url;

        return url[urls.BasePath.Value.Length..];
    }
}
