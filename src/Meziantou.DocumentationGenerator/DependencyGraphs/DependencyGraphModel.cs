using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

internal sealed record DependencyGraphModel(ImmutableArray<DependencyGraphNode> Nodes, ImmutableArray<DependencyGraphEdge> Edges, ImmutableArray<string> UnmatchedPatterns);
