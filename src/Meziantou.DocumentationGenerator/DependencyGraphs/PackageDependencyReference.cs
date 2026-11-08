using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <param name="Frameworks">The target frameworks that declare the dependency, or empty when every target framework declares it.</param>
internal sealed record PackageDependencyReference(string Id, string VersionRange, ImmutableArray<string> Frameworks);
