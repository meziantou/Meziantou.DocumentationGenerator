using System.Net;
using System.Text.Json;
using Meziantou.DocumentationGenerator.Configuration;
using Meziantou.DocumentationGenerator.Content;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Rendering;

/// <summary>
/// Folder URLs (<c>/guides/</c>) have no page of their own: static hosts serve <c>guides/index.html</c> for them, so a small
/// page redirecting to the first page of the folder is generated there, unless another file already uses that path.
/// </summary>
internal static class FolderRedirects
{
    /// <summary>Returns the folders of the content tree, except the root, with the first page they redirect to.</summary>
    public static IEnumerable<(ContentFolder Folder, AuthoredPage FirstPage)> GetFolders(ContentFolder? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<ContentFolder>([root]);
        while (stack.TryPop(out var folder))
        {
            foreach (var child in folder.Entries.OfType<ContentFolder>().Reverse())
            {
                stack.Push(child);
            }

            if (!folder.IsRoot && folder.FirstPage is { } firstPage)
                yield return (folder, firstPage);
        }
    }

    public static string GetFilePath(ContentFolder folder) => folder.RoutePath + "/" + LinkService.HomePath;

    /// <summary>
    /// Builds the redirect page. The target URL is relative to the redirect page, so the site can be relocated. The script
    /// keeps the query and fragment of the requested URL; the refresh meta tag and the link work without JavaScript.
    /// </summary>
    public static string Build(SiteSettings settings, string relativeTargetUrl, string? canonicalUrl, string title)
    {
        var target = WebUtility.HtmlEncode(relativeTargetUrl);
        var canonical = canonicalUrl is null ? "" : "<link rel=\"canonical\" href=\"" + WebUtility.HtmlEncode(canonicalUrl) + "\">";

        // The JSON encoder escapes '<' and '>', so the value cannot close the script element
        var script = "<script>location.replace(" + JsonSerializer.Serialize(relativeTargetUrl) + "+location.search+location.hash)</script>";
        return "<!DOCTYPE html>\n"
            + "<html lang=\"" + WebUtility.HtmlEncode(settings.Language) + "\">\n"
            + "<meta charset=\"utf-8\">\n"
            + "<title>" + WebUtility.HtmlEncode(title) + "</title>\n"
            + canonical + (canonical.Length > 0 ? "\n" : "")
            + "<meta http-equiv=\"refresh\" content=\"0; url=" + target + "\">\n"
            + script + "\n"
            + "<p><a href=\"" + target + "\">" + WebUtility.HtmlEncode(title) + "</a>\n";
    }
}
