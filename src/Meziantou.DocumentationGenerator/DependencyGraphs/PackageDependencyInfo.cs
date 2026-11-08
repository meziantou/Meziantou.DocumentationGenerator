using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

internal sealed record PackageDependencyInfo(string Id, string Version, string? Description, ImmutableArray<PackageDependencyReference> Dependencies);
