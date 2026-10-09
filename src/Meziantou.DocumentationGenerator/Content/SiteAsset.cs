using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>A file copied as-is to the output: a local file or an entry of a package.</summary>
internal sealed record SiteAsset(string OutputPath, FullPath? LocalPath, FullPath? PackagePath, string? PackageEntry)
{
    public string SourceKey => LocalPath is { } local ? "file:" + local.Value : "package:" + PackagePath!.Value.Value + "!" + PackageEntry;
}
