using System.Collections.Concurrent;
using Meziantou.DocumentationGenerator.Diagnostics;

namespace Meziantou.DocumentationGenerator.Links;

/// <summary>
/// Validates the links of the generated site: every internal link must target a generated file and, when it has a
/// fragment, an element with that identifier. Anchors must be unique in each page. Problems in an authored page are
/// reported on its Markdown file.
/// </summary>
internal sealed class SiteLinkValidator(UrlBuilder urls, DiagnosticBag diagnostics)
{
    private readonly Dictionary<string, HashSet<string>> _anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sourcePaths = new(StringComparer.Ordinal);
    private readonly List<(string Page, LinkTarget[] Links)> _pageLinks = [];

    // Most pages of a directory share the same links (e.g. the navigation), so a link is resolved once per directory
    private readonly ConcurrentDictionary<(string Directory, string Href), LinkTarget> _targets = new();
    private readonly Lock _lock = new();

    /// <param name="sourcePath">Display path of the file the page is generated from, such as an authored Markdown file.</param>
    public void AddFile(string filePath, IEnumerable<string> anchorIds, IEnumerable<string> hrefs, string? sourcePath = null)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        List<string>? duplicates = null;
        foreach (var id in anchorIds)
        {
            if (!anchors.Add(id))
            {
                (duplicates ??= []).Add(id);
            }
        }

        var pageUrl = urls.GetUrl(filePath);
        var directory = pageUrl[..(pageUrl.LastIndexOf('/', StringComparison.Ordinal) + 1)];
        var targets = new HashSet<LinkTarget>(ReferenceEqualityComparer.Instance);
        foreach (var href in hrefs)
        {
            // An anchor of the page does not depend on the directory, and a query is relative to the page itself
            var key = href.StartsWith('#', StringComparison.Ordinal) ? ("", href) : href.StartsWith('?', StringComparison.Ordinal) ? (pageUrl, href) : (directory, href);
            if (!_targets.TryGetValue(key, out var target))
            {
                target = _targets.GetOrAdd(key, Resolve(key.Item1, href));
            }

            targets.Add(target);
        }

        lock (_lock)
        {
            if (sourcePath is not null)
            {
                _sourcePaths[filePath] = sourcePath;
            }

            if (duplicates is not null)
            {
                foreach (var id in duplicates)
                {
                    diagnostics.Report(DiagnosticDescriptors.DuplicateAnchor, $"The identifier '{id}' is used more than once in '{filePath}'", GetLocation(filePath));
                }
            }

            _anchors[filePath] = anchors;
            _pageLinks.Add((filePath, [.. targets]));
        }
    }

    public void Validate()
    {
        lock (_lock)
        {
            // The diagnostic bag sorts the diagnostics, so the pages can be validated in any order
            foreach (var (page, targets) in _pageLinks)
            {
                foreach (var target in targets)
                {
                    var error = GetError(page, target);
                    if (error is not null)
                    {
                        diagnostics.Report(DiagnosticDescriptors.BrokenLink, $"Broken link '{target.Href}' in '{page}': {error}", GetLocation(page));
                    }
                }
            }
        }
    }

    private DiagnosticLocation GetLocation(string filePath) => new() { FilePath = _sourcePaths.GetValueOrDefault(filePath, filePath) };

    private string? GetError(string page, LinkTarget target)
    {
        if (target.Error is not null)
            return target.Error;

        if (target.FilePath is null)
            return target.Fragment is null ? null : CheckAnchor(page, target.Fragment);

        if (!_anchors.ContainsKey(target.FilePath))
            return $"'{target.FilePath}' is not generated";

        return target.Fragment is null ? null : CheckAnchor(target.FilePath, target.Fragment);
    }

    /// <summary>Resolves a link of a page; the result does not depend on the other generated files.</summary>
    /// <param name="baseUrl">The URL of the page, or of its directory (ending with a slash) when the link does not depend on the file name.</param>
    private LinkTarget Resolve(string baseUrl, string href)
    {
        if (href.Length == 0)
            return LinkTarget.Invalid(href, "empty link");

        if (href.StartsWith('#', StringComparison.Ordinal))
            return new LinkTarget(href, Error: null, FilePath: null, Uri.UnescapeDataString(href[1..]));

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) && !href.StartsWith('/', StringComparison.Ordinal))
        {
            return absolute.Scheme is "http" or "https" or "mailto" ? LinkTarget.External(href) : LinkTarget.Invalid(href, $"the scheme '{absolute.Scheme}' is not allowed");
        }

        var url = href;
        if (!url.StartsWith('/', StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(new Uri("https://docgen.invalid" + baseUrl), url, out var resolved))
                return LinkTarget.Invalid(href, "invalid relative URL");

            url = resolved.PathAndQuery + resolved.Fragment;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
            return LinkTarget.External(href);

        var (path, fragment) = Split(url);
        if (!path.StartsWith(urls.BasePath.Value, StringComparison.Ordinal))
            return LinkTarget.Invalid(href, $"the link is outside the base path '{urls.BasePath.Value}'");

        var filePath = Uri.UnescapeDataString(path[urls.BasePath.Value.Length..]);
        if (filePath.Length == 0 || filePath.EndsWith('/', StringComparison.Ordinal))
        {
            filePath += "index.html";
        }

        return new LinkTarget(href, Error: null, filePath, fragment);
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

    /// <summary>A resolved link: an error found without the other generated files, or the file and the anchor it targets.</summary>
    /// <param name="FilePath">The targeted file, or <see langword="null"/> for an anchor of the page or an external link.</param>
    private sealed record LinkTarget(string Href, string? Error, string? FilePath, string? Fragment)
    {
        public static LinkTarget Invalid(string href, string error) => new(href, error, FilePath: null, Fragment: null);

        public static LinkTarget External(string href) => new(href, Error: null, FilePath: null, Fragment: null);
    }
}
