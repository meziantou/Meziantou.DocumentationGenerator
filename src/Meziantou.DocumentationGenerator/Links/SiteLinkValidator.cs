using Meziantou.DocumentationGenerator.Diagnostics;

namespace Meziantou.DocumentationGenerator.Links;

/// <summary>
/// Validates the links of the generated site: every internal link must target a generated file and, when it has a
/// fragment, an element with that identifier. Anchors must be unique in each page.
/// </summary>
internal sealed class SiteLinkValidator(UrlBuilder urls, DiagnosticBag diagnostics)
{
    private readonly Dictionary<string, HashSet<string>> _anchors = new(StringComparer.Ordinal);
    private readonly List<(string Page, string Href)> _links = [];
    private readonly Lock _lock = new();

    public void AddFile(string filePath, IEnumerable<string> anchorIds, IEnumerable<string> hrefs)
    {
        lock (_lock)
        {
            var anchors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in anchorIds)
            {
                if (!anchors.Add(id))
                {
                    diagnostics.Report(DiagnosticDescriptors.DuplicateAnchor, $"The identifier '{id}' is used more than once in '{filePath}'", new DiagnosticLocation { FilePath = filePath });
                }
            }

            _anchors[filePath] = anchors;
            foreach (var href in hrefs)
            {
                _links.Add((filePath, href));
            }
        }
    }

    public void Validate()
    {
        lock (_lock)
        {
            foreach (var (page, href) in _links.Distinct().OrderBy(l => l.Page, StringComparer.Ordinal).ThenBy(l => l.Href, StringComparer.Ordinal))
            {
                var error = GetError(page, href);
                if (error is not null)
                {
                    diagnostics.Report(DiagnosticDescriptors.BrokenLink, $"Broken link '{href}' in '{page}': {error}", new DiagnosticLocation { FilePath = page });
                }
            }
        }
    }

    private string? GetError(string page, string href)
    {
        if (href.Length == 0)
            return "empty link";

        if (href.StartsWith('#', StringComparison.Ordinal))
            return CheckAnchor(page, Uri.UnescapeDataString(href[1..]));

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) && !href.StartsWith('/', StringComparison.Ordinal))
        {
            return absolute.Scheme is "http" or "https" or "mailto" ? null : $"the scheme '{absolute.Scheme}' is not allowed";
        }

        if (!href.StartsWith('/', StringComparison.Ordinal))
            return "internal links must be root-relative";

        if (href.StartsWith("//", StringComparison.Ordinal))
            return null;

        var (path, fragment) = Split(href);
        if (!path.StartsWith(urls.BasePath.Value, StringComparison.Ordinal))
            return $"the link is outside the base path '{urls.BasePath.Value}'";

        var filePath = Uri.UnescapeDataString(path[urls.BasePath.Value.Length..]);
        if (filePath.Length == 0 || filePath.EndsWith('/', StringComparison.Ordinal))
        {
            filePath += "index.html";
        }

        if (!_anchors.ContainsKey(filePath))
            return $"'{filePath}' is not generated";

        return fragment is null ? null : CheckAnchor(filePath, fragment);
    }

    private string? CheckAnchor(string filePath, string anchor)
    {
        if (!_anchors.TryGetValue(filePath, out var anchors))
            return null;

        return anchors.Contains(anchor) ? null : $"'{filePath}' has no element with the identifier '{anchor}'";
    }

    private static (string Path, string? Fragment) Split(string href)
    {
        string? fragment = null;
        var hash = href.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            fragment = Uri.UnescapeDataString(href[(hash + 1)..]);
            href = href[..hash];
        }

        var query = href.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            href = href[..query];
        }

        return (href, fragment);
    }
}
