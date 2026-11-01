using System.Text;
using Meziantou.DocumentationGenerator.Configuration;

namespace Meziantou.DocumentationGenerator.Links;

/// <summary>
/// Converts output file paths to site URLs and makes them file-relative when emitting HTML. The base path is applied
/// exactly once, <c>index.html</c> files are linked by their directory URL, and paths and fragments are percent-encoded.
/// </summary>
internal sealed class UrlBuilder(UrlBasePath basePath)
{
    private static readonly System.Buffers.SearchValues<char> SafePathCharacters = System.Buffers.SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~/");

    public UrlBasePath BasePath { get; } = basePath;

    public string GetUrl(string filePath, string? fragment = null, string? query = null)
    {
        var path = filePath;
        if (path == "index.html")
        {
            path = "";
        }
        else if (path.EndsWith("/index.html", StringComparison.Ordinal))
        {
            path = path[..^"index.html".Length];
        }

        var url = BasePath.Value + EncodePath(path);
        if (!string.IsNullOrEmpty(query))
        {
            url += "?" + query;
        }

        if (!string.IsNullOrEmpty(fragment))
        {
            url += "#" + EncodeFragment(fragment);
        }

        return url;
    }

    /// <summary>Converts a site URL to a URL relative to the output file, leaving external URLs and fragments unchanged.</summary>
    public string GetRelativeUrl(string filePath, string url)
    {
        if (!url.StartsWith(BasePath.Value, StringComparison.Ordinal) || url.StartsWith("//", StringComparison.Ordinal))
            return url;

        // Called for every link of every page: the result is built without intermediate strings
        var target = url.AsSpan(BasePath.Value.Length);
        var suffixIndex = target.IndexOfAny('?', '#');
        var suffix = suffixIndex < 0 ? [] : target[suffixIndex..];
        var targetPath = suffixIndex < 0 ? target : target[..suffixIndex];
        var directoryEnd = filePath.LastIndexOf('/', StringComparison.Ordinal);
        var directory = directoryEnd < 0 ? "" : EncodePath(filePath[..(directoryEnd + 1)]);
        var commonLength = 0;
        for (var i = 0; i < Math.Min(directory.Length, targetPath.Length) && directory[i] == targetPath[i]; i++)
        {
            if (directory[i] == '/')
            {
                commonLength = i + 1;
            }
        }

        var depth = directory.AsSpan(commonLength).Count('/');
        var rest = targetPath[commonLength..];
        if (depth == 0 && rest.IsEmpty)
            return string.Concat("./", suffix);

        var length = (depth * 3) + rest.Length + suffix.Length;
        return string.Create(length, (depth, url, Offset: url.Length - rest.Length - suffix.Length), static (span, state) =>
        {
            for (var i = 0; i < state.depth; i++)
            {
                "../".CopyTo(span[(i * 3)..]);
            }

            state.url.AsSpan(state.Offset).CopyTo(span[(state.depth * 3)..]);
        });
    }

    /// <summary>Returns the absolute URL of a page, or <see langword="null"/> when no site URL is configured.</summary>
    public static string? GetAbsoluteUrl(Uri? siteUrl, string url)
    {
        if (siteUrl is null)
            return null;

        return siteUrl.GetLeftPart(UriPartial.Authority) + url;
    }

    public static string EncodePath(string path)
    {
        if (!path.AsSpan().ContainsAnyExcept(SafePathCharacters))
            return path;

        var builder = new StringBuilder(path.Length);
        foreach (var rune in path.EnumerateRunes())
        {
            if (rune.IsAscii && (char.IsAsciiLetterOrDigit((char)rune.Value) || rune.Value is '-' or '.' or '_' or '~' or '/'))
            {
                builder.Append((char)rune.Value);
            }
            else
            {
                AppendEncoded(builder, rune);
            }
        }

        return builder.ToString();
    }

    public static string EncodeFragment(string fragment)
    {
        var builder = new StringBuilder(fragment.Length);
        foreach (var rune in fragment.EnumerateRunes())
        {
            // Characters allowed in a fragment by RFC 3986, except '%' which must be encoded to keep the value unambiguous
            if (rune.IsAscii && (char.IsAsciiLetterOrDigit((char)rune.Value) || rune.Value is '-' or '.' or '_' or '~' or '!' or '$' or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '=' or ':' or '@' or '/' or '?'))
            {
                builder.Append((char)rune.Value);
            }
            else
            {
                AppendEncoded(builder, rune);
            }
        }

        return builder.ToString();
    }

    private static void AppendEncoded(StringBuilder builder, Rune rune)
    {
        Span<byte> bytes = stackalloc byte[4];
        var length = rune.EncodeToUtf8(bytes);
        foreach (var b in bytes[..length])
        {
            builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
