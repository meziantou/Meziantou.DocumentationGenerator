namespace Meziantou.DocumentationGenerator.DependencyGraphs;

/// <param name="Path">SVG path data of the line, which ends at the base of the arrowhead.</param>
/// <param name="Arrow">SVG path data of the arrowhead.</param>
/// <param name="Reversed"><see langword="true"/> when the edge is part of a cycle and is drawn against the direction of the layers.</param>
internal sealed record LayoutEdge(string Path, string Arrow, bool Reversed);
