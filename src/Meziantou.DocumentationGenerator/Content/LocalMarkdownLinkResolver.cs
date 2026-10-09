using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>
/// Resolves relative links of a local Markdown file: links to Markdown pages target the generated page, other files are
/// copied. Targets must be inside <paramref name="allowedRoot"/>.
/// </summary>
internal sealed class LocalMarkdownLinkResolver(
    FullPath file,
    FullPath allowedRoot,
    string outputPrefix,
    string displayPath,
    Func<FullPath, AuthoredPage?> findPage,
    SiteAssetRegistry assets,
    DiagnosticBag diagnostics) : IMarkdownLinkResolver
{
    public string? ResolveRelative(string path, bool isImage, int? line)
    {
        var location = new DiagnosticLocation { FilePath = displayPath, Line = line };
        var target = FullPath.Combine(file.Parent, path.Replace('\\', '/'));
        if (!target.IsChildOf(allowedRoot))
        {
            diagnostics.Report(DiagnosticDescriptors.PathEscapesRoot, $"'{path}' is outside the allowed directory and is not published", location);
            return null;
        }

        if (!isImage && target.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            var page = findPage(target);
            if (page is null)
            {
                diagnostics.Report(DiagnosticDescriptors.ContentFileNotFound, $"'{path}' is not a page of the content directory", location);
                return null;
            }

            return page.Url;
        }

        if (Directory.Exists(target) && !isImage)
        {
            var index = findPage(target / "index.md");
            if (index is not null)
                return index.Url;
        }

        if (!File.Exists(target))
        {
            diagnostics.Report(DiagnosticDescriptors.AssetNotFound, $"'{path}' does not exist", location);
            return null;
        }

        var outputPath = outputPrefix + target.MakePathRelativeTo(allowedRoot).Replace('\\', '/');
        return assets.Register(new SiteAsset(outputPath, target, PackagePath: null, PackageEntry: null), location);
    }
}
