using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Packages;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>
/// Resolves relative links of a README embedded in a package. Files present in the package are copied; other targets
/// link to the repository at the commit recorded in the package metadata. Links are never guessed: without a commit,
/// the link is removed and reported.
/// </summary>
internal sealed class PackageReadmeLinkResolver(
    AcquiredPackage package,
    string readmeEntry,
    IReadOnlySet<string> entries,
    string outputPrefix,
    SiteAssetRegistry assets,
    DiagnosticBag diagnostics) : IMarkdownLinkResolver
{
    public string? ResolveRelative(string path, bool isImage, int? line)
    {
        var displayPath = package.Configuration.Identity + "/" + readmeEntry;
        var location = new DiagnosticLocation { FilePath = displayPath, Line = line, Package = package.Configuration.Identity };
        var directory = readmeEntry.Contains('/', StringComparison.Ordinal) ? readmeEntry[..readmeEntry.LastIndexOf('/', StringComparison.Ordinal)] : "";
        var combined = path.StartsWith('/', StringComparison.Ordinal) ? path.TrimStart('/') : (directory.Length == 0 ? path : directory + "/" + path);
        var entry = NormalizeSegments(combined) is { Length: > 0 } normalized ? PackageArchive.NormalizeEntryName(normalized, out _) : null;
        if (entry is not null)
        {
            var match = entries.FirstOrDefault(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !match.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return assets.Register(new SiteAsset(outputPrefix + match, LocalPath: null, package.Path, match), location);
        }

        var repository = GitHubRepository.TryParse(package.Metadata.RepositoryUrl);
        var commit = package.Metadata.RepositoryCommit;
        if (repository is null || string.IsNullOrEmpty(commit))
        {
            diagnostics.Report(DiagnosticDescriptors.MissingRepositoryMetadata, repository is null
                ? $"'{path}' is not in the package and the package has no GitHub repository URL; the link is removed"
                : $"'{path}' is not in the package and the package has no repository commit; the link is removed", location);
            return null;
        }

        // Paths in the package are relative to the package root, which usually differs from the repository root;
        // the README is assumed to be at the root of the repository, as for nuget.org.
        var repositoryPath = NormalizeSegments(path.TrimStart('/'));
        if (repositoryPath is null)
        {
            diagnostics.Report(DiagnosticDescriptors.PathEscapesRoot, $"'{path}' is outside the repository", location);
            return null;
        }

        return isImage ? repository.GetRawUrl(commit, repositoryPath) : repository.GetBlobUrl(commit, repositoryPath);
    }

    private static string? NormalizeSegments(string path)
    {
        var result = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or ".")
                continue;

            if (segment == "..")
            {
                if (result.Count == 0)
                    return null;

                result.RemoveAt(result.Count - 1);
                continue;
            }

            result.Add(segment);
        }

        return string.Join('/', result);
    }
}
