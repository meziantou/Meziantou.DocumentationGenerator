using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Packages;
using Meziantou.Framework;
using NuGet.Frameworks;
using NuGet.Packaging;

namespace Meziantou.DocumentationGenerator.Discovery;

/// <summary>Documentation inputs found in a package.</summary>
internal sealed class DiscoveredPackage
{
    public required AcquiredPackage Package { get; init; }

    /// <summary>API assets sorted by assembly name then target framework.</summary>
    public required ImmutableArray<ApiAsset> Assets { get; init; }

    /// <summary>Target frameworks whose folder only contains the <c>_._</c> placeholder.</summary>
    public required ImmutableArray<NuGetFramework> PlaceholderFrameworks { get; init; }

    public required ImmutableArray<PackageDependencyGroup> Dependencies { get; init; }

    /// <summary>Source files per target framework when the package is a source package (no compile-time assembly).</summary>
    public ImmutableArray<SourceFolder> SourceFolders { get; init; } = [];

    public bool IsSourcePackage => !SourceFolders.IsEmpty;

    public string Id => Package.Id;

    public DiscoveredPackage WithAssets(ImmutableArray<ApiAsset> assets) => new()
    {
        Package = Package,
        Assets = assets,
        PlaceholderFrameworks = PlaceholderFrameworks,
        Dependencies = Dependencies,
        SourceFolders = SourceFolders,
    };

    public IEnumerable<string> TargetFrameworks => Assets.Select(a => a.TargetFramework).Distinct(StringComparer.Ordinal);
}
