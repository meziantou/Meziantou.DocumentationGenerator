using System.Text;
using System.Text.Json;
using Meziantou.DocumentationGenerator.Api;
using Meziantou.DocumentationGenerator.Links;
using Meziantou.DocumentationGenerator.Rendering;

namespace Meziantou.DocumentationGenerator.Search;

/// <summary>
/// Builds the static search index. Only emitted pages and anchors are indexed. Each item is
/// <c>[title, url, kind, package, qualifiedName, targetFrameworks]</c>, where the URL is relative to the base path and the
/// package is an index in <c>packages</c> (-1 when the item does not belong to a package). Members are indexed once per
/// overload group, so overloads do not duplicate results.
/// </summary>
internal static class SearchIndexBuilder
{
    public const string FileName = "search-index.json";
    public const int Version = 2;

    public static string Build(SiteRenderContext site)
    {
        var packages = site.Catalog.Packages.Where(p => !p.Namespaces.IsEmpty).ToList();
        var items = new List<SearchItem>();

        if (site.Content.HomePage is null)
        {
            items.Add(new SearchItem(site.SiteTitle, LinkService.HomePath, "article", -1, "", ""));
        }

        foreach (var page in site.Content.Pages)
        {
            items.Add(new SearchItem(page.Title, page.Route.FilePath, "article", -1, "", ""));
        }

        for (var packageIndex = 0; packageIndex < packages.Count; packageIndex++)
        {
            var package = packages[packageIndex];
            items.Add(new SearchItem(package.Id, RouteNames.GetPackagePath(package.Id), "package", packageIndex, "", ""));

            foreach (var ns in package.Namespaces)
            {
                items.Add(new SearchItem(ns.IsGlobal ? "Global namespace" : ns.Name, RouteNames.GetNamespacePath(package.Id, ns.Name), "namespace", packageIndex, ns.Name, ""));
            }

            foreach (var type in package.GetAllTypes())
            {
                if (site.Links.GetTypeRoute(type) is not { } route)
                    continue;

                items.Add(new SearchItem(type.DisplayName, route.FilePath, ApiRenderHelper.GetTypeKind(type).ToLowerInvariant(), packageIndex, type.QualifiedDisplayName, Frameworks(type)));
                foreach (var group in type.Members.Where(m => m.Category is not ApiMemberCategory.EnumValue).GroupBy(m => m.OverloadName, StringComparer.Ordinal))
                {
                    var members = group.ToList();
                    var member = members[0];
                    var anchor = members.Count > 1 ? RouteNames.GetOverloadAnchor(group.Key) : RouteNames.GetMemberAnchor(member);
                    var name = member.Category switch
                    {
                        ApiMemberCategory.Constructor => type.PrimaryType.Name,
                        ApiMemberCategory.Operator => ApiRenderHelper.GetMemberDisplayName(member),
                        ApiMemberCategory.Indexer => "this[]",
                        _ => member.Name,
                    };
                    var kind = member.Category switch
                    {
                        ApiMemberCategory.Constructor => "constructor",
                        ApiMemberCategory.Property or ApiMemberCategory.Indexer => "property",
                        ApiMemberCategory.Method => "method",
                        ApiMemberCategory.Operator => "operator",
                        ApiMemberCategory.Event => "event",
                        _ => "field",
                    };

                    items.Add(new SearchItem(type.DisplayName + "." + name, route.FilePath + "#" + UrlBuilder.EncodeFragment(anchor), kind, packageIndex, type.QualifiedDisplayName + "." + name, Frameworks(member)));
                }
            }
        }

        var ordered = items
            .DistinctBy(i => i.Url, StringComparer.Ordinal)
            .OrderBy(i => i.Url, StringComparer.Ordinal)
            .ToList();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("packages");
            foreach (var package in packages)
            {
                writer.WriteStringValue(package.Id);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("items");
            foreach (var item in ordered)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(item.Title);
                writer.WriteStringValue(item.Url);
                writer.WriteStringValue(item.Kind);
                writer.WriteNumberValue(item.Package);
                writer.WriteStringValue(item.QualifiedName);
                writer.WriteStringValue(item.TargetFrameworks);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Target frameworks, only when the symbol is not available in every framework of its package.</summary>
    private static string Frameworks(ApiSymbolModel symbol) => symbol.Availability.IsAvailableInAllTargetFrameworks ? "" : string.Join(' ', symbol.Availability.TargetFrameworks);

    private sealed record SearchItem(string Title, string Url, string Kind, int Package, string QualifiedName, string TargetFrameworks);
}
