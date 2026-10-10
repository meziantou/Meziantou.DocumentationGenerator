using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Glob patterns on '/'-separated relative paths: <c>**</c> matches any directories, <c>*</c> and <c>?</c> match within a segment.</summary>
internal sealed class GlobPattern
{
    private readonly Regex _regex;

    public GlobPattern(string pattern)
    {
        var builder = new StringBuilder("^");
        var value = pattern.Replace('\\', '/').TrimStart('/');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '*' && i + 1 < value.Length && value[i + 1] == '*')
            {
                var followedBySlash = i + 2 < value.Length && value[i + 2] == '/';
                builder.Append(followedBySlash ? "(?:.*/)?" : ".*");
                i += followedBySlash ? 2 : 1;
            }
            else if (c == '*')
            {
                builder.Append("[^/]*");
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        builder.Append('$');
        _regex = new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    public bool IsMatch(string path) => _regex.IsMatch(path);
}
