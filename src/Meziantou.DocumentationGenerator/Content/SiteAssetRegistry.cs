using Meziantou.DocumentationGenerator.Diagnostics;
using Meziantou.DocumentationGenerator.Links;

namespace Meziantou.DocumentationGenerator.Content;

/// <summary>Tracks every copied file, so that output paths are validated before writing and stale files can be cleaned up.</summary>
internal sealed class SiteAssetRegistry(LinkService links)
{
    private readonly Dictionary<string, SiteAsset> _assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public IReadOnlyList<SiteAsset> Assets
    {
        get
        {
            lock (_lock)
            {
                return [.. _assets.Values.OrderBy(a => a.OutputPath, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>Registers the asset and returns its URL, or <see langword="null"/> when its output path is unsafe or already used by another file.</summary>
    public string? Register(SiteAsset asset, DiagnosticLocation? location)
    {
        lock (_lock)
        {
            if (_assets.TryGetValue(asset.OutputPath, out var existing))
            {
                if (string.Equals(existing.SourceKey, asset.SourceKey, StringComparison.Ordinal))
                    return links.Urls.GetUrl(existing.OutputPath);

                links.TryRegister(new SiteRoute(asset.OutputPath, PageKind.Asset, asset.SourceKey), location);
                return null;
            }

            if (!links.TryRegister(new SiteRoute(asset.OutputPath, PageKind.Asset, asset.SourceKey), location))
                return null;

            _assets.Add(asset.OutputPath, asset);
            return links.Urls.GetUrl(asset.OutputPath);
        }
    }
}
