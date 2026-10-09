using Meziantou.Framework.Sanitizers;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>
/// Raw HTML policy for authored Markdown: raw HTML is allowed but reduced to an allow-list. Scripts, styles, frames,
/// forms, event handlers, style attributes and unsafe URL schemes are removed. Classes and identifiers are kept so that
/// highlighting and heading anchors survive.
/// </summary>
internal static class ContentSanitizer
{
    private static readonly string[] AdditionalAttributes =
    [
        "id", "class", "title", "lang", "dir", "role", "aria-label", "aria-hidden", "aria-describedby", "aria-labelledby",
        "target", "rel", "loading", "decoding", "start", "reversed", "type", "checked", "disabled", "colspan", "rowspan",
        "scope", "headers", "align", "valign", "width", "height", "alt", "datetime", "open", "name",
    ];

    private static readonly string[] AdditionalElements = ["details", "summary", "figure", "figcaption", "picture", "source", "input", "kbd", "samp", "var", "mark", "sup", "sub", "abbr", "dl", "dt", "dd", "section"];

    public static string Sanitize(string html)
    {
        var sanitizer = new HtmlSanitizer();
        foreach (var attribute in AdditionalAttributes)
        {
            sanitizer.ValidAttributes.Add(attribute);
        }

        foreach (var element in AdditionalElements)
        {
            sanitizer.ValidElements.Add(element);
        }

        // Only checkboxes rendered by task lists are kept from form elements
        sanitizer.ValidElements.Remove("form");
        sanitizer.ValidElements.Remove("button");
        sanitizer.ValidElements.Remove("textarea");
        sanitizer.ValidElements.Remove("select");
        sanitizer.ValidAttributePrefixes.Clear();
        return sanitizer.SanitizeHtmlFragment(html);
    }

    /// <summary>Returns <see langword="true"/> for http, https, mailto and relative URLs.</summary>
    public static bool IsAllowedUrl(string url)
    {
        if (url.StartsWith('#', StringComparison.Ordinal) || url.StartsWith('/', StringComparison.Ordinal) || url.StartsWith('?', StringComparison.Ordinal))
            return !url.StartsWith("//", StringComparison.Ordinal) || Uri.TryCreate("https:" + url, UriKind.Absolute, out _);

        var colon = url.IndexOf(':', StringComparison.Ordinal);
        var slash = url.IndexOfAny(['/', '?', '#']);
        if (colon < 0 || (slash >= 0 && slash < colon))
            return true;

        var scheme = url[..colon].Trim().ToLowerInvariant();
        return scheme is "http" or "https" or "mailto";
    }
}
