using System.Collections.Frozen;
using System.Collections.Immutable;
using Meziantou.DocumentationGenerator.Discovery;
using NuGet.Packaging;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <summary>The documented packages and their NuGet dependencies, used to draw dependency graphs.</summary>
internal sealed class PackageDependencyIndex
{
    private readonly FrozenDictionary<string, PackageDependencyInfo> _packagesById;

    public static PackageDependencyIndex Empty { get; } = new([]);

    public PackageDependencyIndex(IEnumerable<PackageDependencyInfo> packages)
    {
        Packages = [.. packages.DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id, StringComparer.Ordinal)];
        _packagesById = Packages.ToFrozenDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Packages sorted by identifier.</summary>
    public ImmutableArray<PackageDependencyInfo> Packages { get; }

    public bool TryGet(string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out PackageDependencyInfo? package) => _packagesById.TryGetValue(id, out package);

    public static PackageDependencyIndex Create(IEnumerable<DiscoveredPackage> packages)
        => new(packages.Select(p => new PackageDependencyInfo(p.Id, p.Package.Metadata.Version.ToNormalizedString(), p.Package.Metadata.Description, GetDependencies(p.Dependencies))));

    /// <summary>Merges the dependency groups: a dependency lists the target frameworks it applies to, or none when it applies to all of them.</summary>
    internal static ImmutableArray<PackageDependencyReference> GetDependencies(IEnumerable<PackageDependencyGroup> groups)
    {
        var groupList = groups.ToList();
        var dependencies = new Dictionary<string, (string Id, List<string> Versions, List<string> Frameworks)>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groupList)
        {
            var framework = group.TargetFramework.IsAny ? "any" : group.TargetFramework.GetShortFolderName();
            foreach (var dependency in group.Packages)
            {
                if (!dependencies.TryGetValue(dependency.Id, out var entry))
                {
                    entry = (dependency.Id, [], []);
                    dependencies.Add(dependency.Id, entry);
                }

                var version = dependency.VersionRange.PrettyPrint();
                if (!entry.Versions.Contains(version, StringComparer.Ordinal))
                {
                    entry.Versions.Add(version);
                }

                if (!entry.Frameworks.Contains(framework, StringComparer.Ordinal))
                {
                    entry.Frameworks.Add(framework);
                }
            }
        }

        return [.. dependencies.Values
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => new PackageDependencyReference(d.Id, string.Join(", ", d.Versions), d.Frameworks.Count == groupList.Count ? [] : [.. d.Frameworks]))];
    }
}
