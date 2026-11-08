using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <summary>An arrow from a package (<see cref="From"/>) to one of its dependencies (<see cref="To"/>), as indexes of <see cref="DependencyGraphModel.Nodes"/>.</summary>
internal sealed record DependencyGraphEdge(int From, int To, string VersionRange, ImmutableArray<string> Frameworks);
