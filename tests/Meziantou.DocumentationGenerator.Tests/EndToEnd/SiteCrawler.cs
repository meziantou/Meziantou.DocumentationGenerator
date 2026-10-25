using System.Text.Json;
using System.Xml.Linq;
using Meziantou.DocumentationGenerator.Tests.Infrastructure;
using Meziantou.Framework;
using Element = Meziantou.Framework.WebPlatform.Dom.Element;

namespace Meziantou.DocumentationGenerator.Tests.EndToEnd;

/// <summary>
/// Crawls a generated site independently from the generator's own validation: every internal link, asset, fragment,
/// search index entry and sitemap entry must resolve, and identifiers must be unique in each page.
/// </summary>
internal static class SiteCrawler
{
    public static IReadOnlyList<string> Crawl(FullPath root, string basePath, string? siteUrl)
    {
        var errors = new List<string>();
        var ids = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var links = new List<(string Page, string Url)>();
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in files.Where(f => f.EndsWith(".html", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var document = HtmlDocumentLoader.Parse(File.ReadAllText(root / file));
            var pageIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in document.QuerySelectorAll<Element>("[id]"))
            {
                if (!pageIds.Add(element.Id!))
                {
                    errors.Add($"{file}: duplicate id '{element.Id}'");
                }
            }

            ids[file] = pageIds;
            foreach (var element in document.QuerySelectorAll<Element>("a[href], link[href], img[src], script[src]"))
            {
                var url = element.GetAttribute("href") ?? element.GetAttribute("src")!;
                if (element.LocalName == "link" && element.GetAttribute("rel") is "canonical")
                {
                    if (siteUrl is null || !url.StartsWith(siteUrl + basePath, StringComparison.Ordinal))
                    {
                        errors.Add($"{file}: invalid canonical URL '{url}'");
                    }

                    continue;
                }

                links.Add((file, url));
            }

            if (document.QuerySelector("[data-index]")?.GetAttribute("data-index") is { } index)
            {
                links.Add((file, index));
            }
        }

        if (files.Contains("_assets/search-index.json"))
        {
            using var index = JsonDocument.Parse(File.ReadAllText(root / "_assets/search-index.json"));
            foreach (var item in index.RootElement.GetProperty("items").EnumerateArray())
            {
                links.Add(("_assets/search-index.json", basePath + item[1].GetString()));
            }
        }

        if (files.Contains("sitemap.xml"))
        {
            foreach (var location in XDocument.Load(root / "sitemap.xml").Descendants().Where(e => e.Name.LocalName == "loc"))
            {
                if (siteUrl is null || !location.Value.StartsWith(siteUrl, StringComparison.Ordinal) || location.Value.Contains('#', StringComparison.Ordinal))
                {
                    errors.Add($"sitemap.xml: invalid entry '{location.Value}'");
                    continue;
                }

                links.Add(("sitemap.xml", location.Value[siteUrl.Length..]));
            }
        }

        foreach (var (page, url) in links)
        {
            var error = Check(page, url, basePath, files, ids);
            if (error is not null)
            {
                errors.Add($"{page}: '{url}' {error}");
            }
        }

        return errors;
    }

    private static string? Check(string page, string url, string basePath, HashSet<string> files, Dictionary<string, HashSet<string>> ids)
    {
        if (url.StartsWith('#', StringComparison.Ordinal))
            return ids[page].Contains(Uri.UnescapeDataString(url[1..])) ? null : "targets a missing anchor";

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && !url.StartsWith('/', StringComparison.Ordinal))
            return absolute.Scheme is "https" or "http" or "mailto" ? null : "uses an unsafe scheme";

        if (!url.StartsWith(basePath, StringComparison.Ordinal))
            return "is outside the base path";

        var fragment = (string?)null;
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            fragment = Uri.UnescapeDataString(url[(hash + 1)..]);
            url = url[..hash];
        }

        var path = Uri.UnescapeDataString(url[basePath.Length..]);
        if (path.Length == 0 || path.EndsWith('/', StringComparison.Ordinal))
        {
            path += "index.html";
        }

        if (!files.Contains(path))
            return "targets a missing file";

        if (fragment is not null && (!ids.TryGetValue(path, out var targetIds) || !targetIds.Contains(fragment)))
            return "targets a missing anchor";

        return null;
    }
}
