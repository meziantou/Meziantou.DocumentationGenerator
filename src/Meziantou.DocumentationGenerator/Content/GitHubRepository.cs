namespace Meziantou.DocumentationGenerator.Content;

internal sealed record GitHubRepository(string Owner, string Name)
{
    public static GitHubRepository? TryParse(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "git"))
            return null;

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) && !string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase))
            return null;

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || segments[0].Length == 0 || segments[1].Length == 0)
            return null;

        var name = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segments[1][..^4] : segments[1];
        return new GitHubRepository(segments[0], name);
    }

    public string GetBlobUrl(string commit, string path) => $"https://github.com/{Owner}/{Name}/blob/{commit}/{Links.UrlBuilder.EncodePath(path)}";

    public string GetRawUrl(string commit, string path) => $"https://raw.githubusercontent.com/{Owner}/{Name}/{commit}/{Links.UrlBuilder.EncodePath(path)}";
}
