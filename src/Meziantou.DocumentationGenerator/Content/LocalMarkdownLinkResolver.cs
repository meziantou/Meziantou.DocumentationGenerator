using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>
/// Resolves relative links of a local Markdown file: links to Markdown pages target the generated page, links to folders
/// target their first page, and other files are copied next to the pages of their folder. Targets must be inside <paramref name="allowedRoot"/>. A missing
/// target is an error: the link is removed and the build fails.
/// </summary>
internal sealed class LocalMarkdownLinkResolver(
    FullPath file,
    FullPath allowedRoot,
    string displayPath,
    Func<FullPath, AuthoredPage?> findPage,
    Func<FullPath, string?> findFolderUrl,
    Func<FullPath, string> getAssetOutputPath,
    SiteAssetRegistry assets,
    DiagnosticBag diagnostics) : IMarkdownLinkResolver
{
    public string? ResolveRelative(string path, bool isImage, int? line)
    {
        var location = new DiagnosticLocation { FilePath = displayPath, Line = line };
        var target = FullPath.Combine(file.Parent, path.Replace('\\', '/'));
        if (target != allowedRoot && !target.IsChildOf(allowedRoot))
        {
            diagnostics.Report(DiagnosticDescriptors.PathEscapesRoot, $"'{path}' is outside the allowed directory and is not published", location);
            return null;
        }

        if (!isImage && target.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            var page = findPage(target);
            if (page is null)
            {
                var reason = File.Exists(target) ? "is excluded from the content directory" : "does not exist";
                diagnostics.Report(DiagnosticDescriptors.ContentFileNotFound, $"'{path}' {reason}", location);
                return null;
            }

            return page.Url;
        }

        if (Directory.Exists(target))
        {
            var url = isImage ? null : findFolderUrl(target);
            if (url is null)
            {
                diagnostics.Report(DiagnosticDescriptors.ContentFileNotFound, $"'{path}' is a folder without page", location);
            }

            return url;
        }

        if (!File.Exists(target))
        {
            diagnostics.Report(DiagnosticDescriptors.AssetNotFound, $"'{path}' does not exist", location);
            return null;
        }

        return assets.Register(new SiteAsset(getAssetOutputPath(target), target), location);
    }
}
