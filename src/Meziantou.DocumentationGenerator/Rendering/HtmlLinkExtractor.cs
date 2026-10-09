using System.Net;
using System.Text.RegularExpressions;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>Extracts identifiers and link targets from generated HTML for validation.</summary>
internal static partial class HtmlLinkExtractor
{
    public static IEnumerable<string> GetIds(string html) => IdRegex().Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups["value"].Value));

    public static IEnumerable<string> GetLinks(string html) => LinkRegex().Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups["value"].Value));

    [GeneratedRegex("""<[a-zA-Z][^<>]*?\sid="(?<value>[^"]*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10000)]
    private static partial Regex IdRegex();

    [GeneratedRegex("""<(?:a|link|img|script|source)\b[^<>]*?\s(?:href|src)="(?<value>[^"]*)""", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 10000)]
    private static partial Regex LinkRegex();
}
