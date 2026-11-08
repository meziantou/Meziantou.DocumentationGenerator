using System.Collections.Immutable;

namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <param name="IsolatedTop">Top of the grid of nodes without edges, when the graph also has nodes with edges.</param>
internal sealed record LayoutResult(double Width, double Height, ImmutableArray<LayoutRect> Nodes, ImmutableArray<LayoutEdge> Edges, double? IsolatedTop);
