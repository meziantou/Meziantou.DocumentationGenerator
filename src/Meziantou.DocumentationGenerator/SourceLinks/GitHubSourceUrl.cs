using System.Text.RegularExpressions;

namespace Meziantou.DocumentationGenerator.SourceLinks;

/// <summary>Converts Source Link URLs of GitHub repositories to browsable, commit-pinned URLs.</summary>
internal static partial class GitHubSourceUrl
{
    /// <summary>
    /// <c>https://raw.githubusercontent.com/{owner}/{repo}/{commit}/{path}</c> and <c>https://github.com/{owner}/{repo}/raw/{commit}/{path}</c>
    /// become <c>https://github.com/{owner}/{repo}/blob/{commit}/{path}</c>. The revision is the one recorded in the PDB.
    /// </summary>
    public static bool TryConvert(string sourceLinkUrl, out string url, out string path)
    {
        url = "";
        path = "";
        var match = RawRegex().Match(sourceLinkUrl);
        if (!match.Success)
        {
            match = GitHubRawRegex().Match(sourceLinkUrl);
        }

        if (!match.Success)
            return false;

        path = Uri.UnescapeDataString(match.Groups["path"].Value);
        url = $"https://github.com/{match.Groups["owner"].Value}/{match.Groups["repo"].Value}/blob/{match.Groups["commit"].Value}/{match.Groups["path"].Value}";
        return true;
    }

    [GeneratedRegex("^https://raw\\.githubusercontent\\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/(?<commit>[^/]+)/(?<path>[^?#]+)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RawRegex();

    [GeneratedRegex("^https://github\\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/raw/(?<commit>[^/]+)/(?<path>[^?#]+)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitHubRawRegex();
}
