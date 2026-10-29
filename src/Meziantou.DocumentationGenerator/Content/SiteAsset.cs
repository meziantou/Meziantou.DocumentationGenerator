using Meziantou.Framework;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>A local file copied as-is to the output.</summary>
internal sealed record SiteAsset(string OutputPath, FullPath LocalPath)
{
    public string SourceKey => "file:" + LocalPath.Value;
}
